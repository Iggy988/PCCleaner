using System.Windows.Data;
using System.Windows.Markup;

namespace PCCleaner.Services.Localization;

/// <summary>
/// XAML: <c>Text="{loc:Tr Dashboard_Title}"</c>. Produces a one-way binding to
/// <see cref="LocalizationService"/>'s indexer, so the text follows live language changes.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension()
    {
    }

    public TrExtension(string key)
    {
        Key = key;
    }

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = LocalizationService.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
