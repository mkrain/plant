using System.Collections.Generic;

namespace Plant.Tests.TestModels
{
  public class House
  {
    public string Color;
    public int SquareFoot;
    public string Summary { get; set; }

    public House()
    {
          
    }

      public ICollection<Person> Persons { get; set; }

    public House(string color, int squareFoot)
    {
      Color = color;
      SquareFoot = squareFoot;
    }
  }
}