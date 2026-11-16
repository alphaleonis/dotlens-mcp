namespace CSharp15TestProject;

public record Circle(double Radius);

public record Square(double Side);

public union Shape(Circle, Square);

public static class ShapeMath
{
   public static double Area(Shape shape) => shape switch
   {
      Circle circle => Math.PI * circle.Radius * circle.Radius,
      Square square => square.Side * square.Side,
   };
}
