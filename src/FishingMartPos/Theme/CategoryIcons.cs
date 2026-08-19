using System.Windows;
using System.Windows.Media;

namespace FishingMartPos.Theme;

public static class CategoryIcons
{
    public static Geometry GetGeometry(string code) => code switch
    {
        "BAIT" => Bait.Value,
        "FLOAT" => Float.Value,
        "HOOK" => Hook.Value,
        "LIFE" => Life.Value,
        "SEAFOOD" => Seafood.Value,
        "HAT" => Hat.Value,
        "ICE" => Ice.Value,
        "DRINK" => Drink.Value,
        _ => Geometry.Empty,
    };

    private static readonly Lazy<Geometry> Bait = new(() =>
    {
        var figure = new PathFigure { StartPoint = new Point(10, 2), IsClosed = true };
        figure.Segments.Add(new QuadraticBezierSegment(new Point(17, 8), new Point(10, 18), true));
        figure.Segments.Add(new QuadraticBezierSegment(new Point(3, 8), new Point(10, 2), true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return (Geometry)geometry;
    });

    private static readonly Lazy<Geometry> Float = new(() =>
    {
        var group = new GeometryGroup();
        group.Children.Add(new EllipseGeometry(new Point(10, 7), 4, 4));
        group.Children.Add(new LineGeometry(new Point(10, 11), new Point(10, 18)));
        group.Freeze();
        return (Geometry)group;
    });

    private static readonly Lazy<Geometry> Hook = new(() =>
    {
        var figure = new PathFigure { StartPoint = new Point(6, 3) };
        figure.Segments.Add(new LineSegment(new Point(6, 12), true));
        figure.Segments.Add(new ArcSegment(new Point(11, 15), new Size(4, 4), 0, false, SweepDirection.Clockwise, true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return (Geometry)geometry;
    });

    private static readonly Lazy<Geometry> Life = new(() =>
    {
        var group = new GeometryGroup();
        group.Children.Add(new RectangleGeometry(new Rect(4, 7, 12, 9), 1, 1));
        group.Children.Add(new LineGeometry(new Point(4, 10), new Point(16, 10)));
        group.Children.Add(new LineGeometry(new Point(8, 7), new Point(8, 4)));
        group.Children.Add(new LineGeometry(new Point(12, 7), new Point(12, 4)));
        group.Children.Add(new LineGeometry(new Point(8, 4), new Point(12, 4)));
        group.Freeze();
        return (Geometry)group;
    });

    private static readonly Lazy<Geometry> Seafood = new(() =>
    {
        var figure = new PathFigure { StartPoint = new Point(3, 10), IsClosed = true };
        figure.Segments.Add(new LineSegment(new Point(13, 6), true));
        figure.Segments.Add(new LineSegment(new Point(17, 10), true));
        figure.Segments.Add(new LineSegment(new Point(13, 14), true));
        figure.Segments.Add(new LineSegment(new Point(3, 10), true));
        var body = new PathGeometry();
        body.Figures.Add(figure);
        var group = new GeometryGroup();
        group.Children.Add(body);
        group.Children.Add(new EllipseGeometry(new Point(6, 10), 0.7, 0.7));
        group.Freeze();
        return (Geometry)group;
    });

    private static readonly Lazy<Geometry> Hat = new(() =>
    {
        var group = new GeometryGroup();
        group.Children.Add(new EllipseGeometry(new Point(10, 14), 8, 2.2));
        var crownFigure = new PathFigure { StartPoint = new Point(4, 14) };
        crownFigure.Segments.Add(new ArcSegment(new Point(16, 14), new Size(6, 8), 0, false, SweepDirection.Counterclockwise, true));
        var crown = new PathGeometry();
        crown.Figures.Add(crownFigure);
        group.Children.Add(crown);
        group.Freeze();
        return (Geometry)group;
    });

    private static readonly Lazy<Geometry> Ice = new(() =>
    {
        var group = new GeometryGroup();
        group.Children.Add(new EllipseGeometry(new Point(10, 7), 5, 4));
        var cone = new PathFigure { StartPoint = new Point(5, 9), IsClosed = true };
        cone.Segments.Add(new LineSegment(new Point(15, 9), true));
        cone.Segments.Add(new LineSegment(new Point(10, 18), true));
        var coneGeometry = new PathGeometry();
        coneGeometry.Figures.Add(cone);
        group.Children.Add(coneGeometry);
        group.Freeze();
        return (Geometry)group;
    });

    private static readonly Lazy<Geometry> Drink = new(() =>
    {
        var group = new GeometryGroup();
        group.Children.Add(new RectangleGeometry(new Rect(6, 8, 8, 10), 1.5, 1.5));
        group.Children.Add(new RectangleGeometry(new Rect(8, 3, 4, 6)));
        group.Freeze();
        return (Geometry)group;
    });
}
