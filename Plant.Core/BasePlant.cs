using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Properties = System.Collections.Generic.IDictionary<Plant.Core.PropertyData, object>;
using Blueprints = System.Collections.Generic.Dictionary<System.Type, System.Collections.Generic.IDictionary<Plant.Core.PropertyData, object>>;
using Variations = System.Collections.Generic.Dictionary<string, System.Collections.Generic.IDictionary<Plant.Core.PropertyData, object>>;
using CreatedBlueprints = System.Collections.Generic.Dictionary<string, object>;

namespace Plant.Core
{
  #region Events

    public class BluePrintEventArgs : EventArgs
    {
        private object _objectConstructed;

        public BluePrintEventArgs(object objectConstructed)
        {
            this._objectConstructed = objectConstructed;
        }

        public object ObjectConstructed
        {
            get
            {
                return _objectConstructed;
            }
        }
    }

    public delegate void BluePrintCreatedEventHandler(object sender, BluePrintEventArgs e);
    #endregion

  public class BasePlant
  {
    private readonly Variations propertyVariations = new Variations();
    private readonly Blueprints propertyBlueprints = new Blueprints();
    private readonly Blueprints constructorBlueprints = new Blueprints();
    private readonly CreatedBlueprints createdBluePrints = new CreatedBlueprints();
    private readonly IDictionary<Type, CreationStrategy> creationStrategies = new Dictionary<Type, CreationStrategy>();
    private readonly IDictionary<Type, object> postBuildActions = new Dictionary<Type, object>();
    private readonly IDictionary<string, object> postBuildVariationActions = new Dictionary<string, object>();
    private readonly IDictionary<Type, int> sequenceValues = new Dictionary<Type, int>();

    #region BluePrintCreated Event

    public event BluePrintCreatedEventHandler BluePrintCreated;

    protected virtual void OnBluePrintCreated(BluePrintEventArgs e)
    {
        if (BluePrintCreated != null)
            BluePrintCreated(this, e);
    }

    #endregion

    private T CreateViaProperties<T>(Properties userProperties)
    {
      var instance = CreateInstanceWithEmptyConstructor<T>();
      SetProperties(Merge(propertyBlueprints[typeof (T)], userProperties), instance);
      return instance;
    }

    private T CreateViaConstructor<T>(Properties userProperties)
    {
      var type = typeof(T);
      var constructor = type.GetConstructors().First();
      var paramNames = constructor.GetParameters().Select(p => p.Name.ToLower()).ToList();
      var defaultProperties = constructorBlueprints[type];

      var props = Merge(defaultProperties, userProperties);

      return (T) constructor.Invoke(
          props.Keys.OrderBy(prop => paramNames.IndexOf(prop.Name.ToLower())).
          Select(prop => GetPropertyValue<T>(props[prop])).ToArray());
    }

    private object GetPropertyValue<T>(object property)
    {
        if (typeof(ILazyProperty).IsAssignableFrom(property.GetType()))
            return ((ILazyProperty)property).Func.DynamicInvoke();
        if (typeof(ISequence).IsAssignableFrom(property.GetType()))
            return ((ISequence)property).Func.DynamicInvoke(sequenceValues[typeof(T)]++);
        return property;
    }

    private Properties Merge(Properties defaults, Properties overrides)
    {
      return defaults.Keys.Union(overrides.Keys).ToDictionary(key => key,
                            key => overrides.ContainsKey(key) ? overrides[key] : defaults[key]);
    }

    private static T CreateInstanceWithEmptyConstructor<T>()
    {
      return Activator.CreateInstance<T>();
    }

    public virtual T CreateForChild<T>()
    {
        return Create<T>(null, null);
    }

    public virtual T Build<T>(string variation = null)
    {
        return Create<T>(null, variation, false);
    }

    public virtual T Build<T>(T userSpecifiedProperties)
    {
        return Create<T>((object)userSpecifiedProperties, null, false);
    }

    public virtual T Create<T>(string variation)
    {
        return Create<T>(null, variation);
    }

    public virtual T Create<T>(T userSpecifiedProperties)
    {
        return Create<T>((object)userSpecifiedProperties);
    }

    public virtual T Create<T>(object userSpecifiedProperties = null, string variation = null, bool created = true)
    {
      // If the object already exist, let's retrieve it and not generate it again.
      // We need to see how to deal with Sequence and Lazy properties, as they will not work with this.
      // But this feature allows to quickly create objects, related to each other in the Plants.
      string bluePrintKey = BluePrintKey<T>(variation);
      if (createdBluePrints.ContainsKey(bluePrintKey))
          return (T)createdBluePrints[bluePrintKey];

      var userSpecifiedPropertyList = ToPropertyList(userSpecifiedProperties);

      T constructedObject = default(T);
      if(StrategyFor<T>() == CreationStrategy.Constructor)
        constructedObject = CreateViaConstructor<T>(userSpecifiedPropertyList);
      else
        constructedObject = CreateViaProperties<T>(userSpecifiedPropertyList);

      // We should check if for the object properties we have a creation strategy and call create on that one.
      // Also if the property has a value, don't override.
      foreach(var prop in constructedObject.GetType().GetProperties())
      {
          if (StrategyFor(prop.PropertyType) == null || prop.GetValue(constructedObject, null) != null)
              continue;
          
          var value = this.GetType().
              GetMethod("CreateForChild").
              MakeGenericMethod(prop.PropertyType).
              Invoke(this, null);

          prop.SetValue(constructedObject, value, null);
      }

      UpdateProperties<T>(constructedObject, variation);

      if (postBuildActions.ContainsKey(typeof(T)))
        ((Action<T>)postBuildActions[typeof (T)])(constructedObject);

      if (postBuildVariationActions.ContainsKey(bluePrintKey))
          ((Action<T>)postBuildVariationActions[bluePrintKey])(constructedObject);

      if (created)
          OnBluePrintCreated(new BluePrintEventArgs(constructedObject));

      createdBluePrints.Add(bluePrintKey, constructedObject);
      return constructedObject;
    }

      private static string BluePrintKey<T>(string variation)
      {
          return string.Format("{0}-{1}", typeof (T), variation);
      }

      private void UpdateProperties<T>(T constructedObject, string variation)
    {
        if (string.IsNullOrEmpty(variation))
            return;

        SetProperties(propertyVariations[BluePrintKey<T>(variation)], constructedObject);
    } 

    private CreationStrategy StrategyFor<T>()
    {
      if(creationStrategies.ContainsKey(typeof(T)))
        return creationStrategies[typeof (T)];
      throw new TypeNotSetupException(string.Format("No creation strategy defined for type: {0}", typeof(T)));
    }

    private CreationStrategy? StrategyFor(Type t)
    {
        if (creationStrategies.ContainsKey(t))
            return creationStrategies[t];
        return null;
    }

    private void SetProperties<T>(Properties properties, T instance)
    {
      properties.Keys.ToList().ForEach(property =>
        {
            var instanceProperty = instance.GetType().GetProperties().FirstOrDefault(prop => prop.Name == property.Name);
            if (instanceProperty == null) throw new PropertyNotFoundException(property.Name, properties[property]);

            var value = properties[property];
            if (value == null)
                return;
            if (typeof(ILazyProperty).IsAssignableFrom(value.GetType()))
              AssignLazyPropertyResult(instance, instanceProperty, value);
            else if(typeof(ISequence).IsAssignableFrom(value.GetType()))
              AssignSequenceResult(instance, instanceProperty, value, sequenceValues[typeof(T)]);
            else
              instanceProperty.SetValue(instance, value, null);
        });
        sequenceValues[typeof (T)]++;
    }

      private static void AssignSequenceResult<T>(T instance, PropertyInfo instanceProperty, object value, int sequenceValue)
      {
          var sequence = (ISequence) value;

          if (sequence.Func.Method.ReturnType != instanceProperty.PropertyType)
              throw new LazyPropertyHasWrongTypeException(string.Format("Cannot assign type {0} to property {1} of type {2}",
                sequence.Func.Method.ReturnType,
                instanceProperty.Name,
                instanceProperty.PropertyType));
          // I can pass in the instance as a parameter to this function, but only if I'm using property-setters
          instanceProperty.SetValue(instance, sequence.Func.DynamicInvoke(sequenceValue), null);      
      }

    private static void AssignLazyPropertyResult<T>(T instance, PropertyInfo instanceProperty, object value)
    {
      var lazyProperty = (ILazyProperty)value;

      if (lazyProperty.Func.Method.ReturnType != instanceProperty.PropertyType)
        throw new LazyPropertyHasWrongTypeException(string.Format("Cannot assign type {0} to property {1} of type {2}",
          lazyProperty.Func.Method.ReturnType,
          instanceProperty.Name,
          instanceProperty.PropertyType));
        // I can pass in the instance as a parameter to this function, but only if I'm using property-setters
      instanceProperty.SetValue(instance, lazyProperty.Func.DynamicInvoke(), null);
    }

      /// <summary>
      /// The post-build method will accept the object that was just constructed as a parameter, so that you
      /// can assign other values to it. Unfortunately, you have to do the casting yourself, for now.
      /// </summary>
      /// <typeparam name="T"></typeparam>
      /// <param name="defaults"></param>
      /// <param name="afterPropertyPopulation"></param>
    public virtual void DefinePropertiesOf<T>(object defaults, Action<T> afterPropertyPopulation)
    {
        DefinePropertiesOf<T>(defaults);
        postBuildActions[typeof (T)] = afterPropertyPopulation;
    }
    
    public virtual void DefinePropertiesOf<T>(T defaults)
    {
        DefinePropertiesOf<T>((object)defaults);
    }

    public virtual void DefinePropertiesOf<T>(T defaults, Action<T> afterPropertyPopulation)
    {
        DefinePropertiesOf<T>((object)defaults);
        postBuildActions[typeof(T)] = afterPropertyPopulation;
    }

    public virtual void DefinePropertiesOf<T>(object defaults)
    {
        creationStrategies.Add(typeof(T), CreationStrategy.Property);
        AddDefaultsTo<T>(propertyBlueprints, defaults);
        sequenceValues.Add(typeof(T), 0);
    }

    public virtual void DefineVariationOf<T>(string variation, T defaults)
    {
        DefineVariationOf<T>(variation, (object)defaults);
    }

    public virtual void DefineVariationOf<T>(string variation, T defaults, Action<T> afterPropertyPopulation)
    {
        DefineVariationOf<T>(variation, (object)defaults);
        postBuildVariationActions[BluePrintKey<T>(variation)] = afterPropertyPopulation;
    }

    public virtual void DefineVariationOf<T>(string variation, object defaults)
    {
        propertyVariations.Add(BluePrintKey<T>(variation), ToPropertyList(defaults));
    }

    public virtual void DefineVariationOf<T>(string variation, object defaults, Action<T> afterPropertyPopulation)
    {
        string hash = BluePrintKey<T>(variation);
        propertyVariations.Add(hash, ToPropertyList(defaults));
        postBuildVariationActions[hash] = afterPropertyPopulation;
    }

    public void DefineConstructionOf<T>(object defaults, Action<T> afterCtorPopulation)
    {
        DefineConstructionOf<T>(defaults);
        postBuildActions[typeof(T)] = afterCtorPopulation;
    }
      public void DefineConstructionOf<T>(object defaults)
    {
      creationStrategies.Add(typeof(T), CreationStrategy.Constructor);
      AddDefaultsTo<T>(constructorBlueprints, defaults);
      sequenceValues.Add(typeof(T), 0);
    }

    private void AddDefaultsTo<T>(Blueprints blueprints, object defaults)
    {
      blueprints.Add(typeof(T), ToPropertyList(defaults));
    }

    private IDictionary<PropertyData, object> ToPropertyList(object obj)
    {
      if(obj == null) return new Dictionary<PropertyData, object>();
      return obj.GetType().GetProperties().ToDictionary(prop => new PropertyData(prop), prop => prop.GetValue(obj, null));
    }

    public BasePlant WithBlueprintsFromAssemblyOf<T>()
    {
      var assembly = typeof(T).Assembly;
      var blueprintTypes = assembly.GetTypes().Where(t => typeof(IBlueprint).IsAssignableFrom(t));
      blueprintTypes.ToList().ForEach(blueprintType =>
                                    {
                                      var blueprint = (IBlueprint)Activator.CreateInstance(blueprintType);
                                      blueprint.SetupPlant(this);
                                    });
      return this;

    }

  }
}
