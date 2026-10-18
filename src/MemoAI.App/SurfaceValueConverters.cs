using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace MemoAI.App;

public sealed class UniformSurfaceRadiusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => new CornerRadius(System.Convert.ToDouble(value));
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
public sealed class UniformSurfaceThicknessConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => new Thickness(System.Convert.ToDouble(value));
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
public sealed class NegativeSurfaceThicknessConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value is Thickness t ? new Thickness(-t.Left, -t.Top, -t.Right, -t.Bottom) : new Thickness();
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
