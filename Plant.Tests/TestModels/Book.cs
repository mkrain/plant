namespace Plant.Tests.TestModels
{
  public class Book
  {
    public string Author  { get; set; }
    public string Publisher { get; set; }

    public Book(string author, string publisher)
    {
      Author = author;
      Publisher = publisher;
    }
  }
}