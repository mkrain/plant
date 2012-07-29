using System.Collections.Generic;

namespace Plant.Tests.TestModels
{
  public class House
  {
    public string Color { get; set; }
    public int SquareFoot { get; set; }
    public string Summary { get; set; }

    public House(string color, int squareFoot)
    {
        Color = color;
        SquareFoot = squareFoot;
    }

    public House()
    {
        Persons = new HashSet<Person>();
    }

    public ICollection<Person> Persons { get; set; }
  }
}