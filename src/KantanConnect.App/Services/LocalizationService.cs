using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;

namespace KantanConnect.App.Services;

/// <summary>
/// Punto único de acceso a los textos localizados de la app (ES/EN), leídos de
/// <c>Resources/Strings.resx</c> (inglés, cultura por defecto) y
/// <c>Resources/Strings.es.resx</c> (español neutro). Se usa <see cref="ResourceManager"/>
/// directamente en vez de la clase fuertemente tipada que Visual Studio generaría
/// automáticamente (<c>ResXFileCodeGenerator</c>), porque esta última depende de
/// herramientas de diseño y no se genera de forma confiable con <c>dotnet build</c> puro
/// — este servicio funciona igual sin importar qué IDE (o ninguno) se use para compilar.
/// </summary>
public sealed class LocalizationService : INotifyPropertyChanged
{
    /// <summary>Instancia única compartida por toda la app (ver uso vía <c>{Binding Source=...}</c> en XAML).</summary>
    public static LocalizationService Instance { get; } = new();

    private readonly ResourceManager _resourceManager =
        new("KantanConnect.App.Resources.Strings", typeof(LocalizationService).Assembly);

    private CultureInfo _currentCulture = CultureInfo.CurrentUICulture;

    private LocalizationService()
    {
    }

    /// <summary>
    /// Notifica con <c>propertyName = null</c> (o vacío) cada vez que cambia el idioma:
    /// es la convención de WPF para "refrescá TODOS los bindings de este objeto", así
    /// no hace falta declarar (ni mantener sincronizada) una propiedad C# por cada
    /// clave del .resx — el indexador de abajo (<see cref="this[string]"/>) alcanza.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>El idioma activo. Cambiarlo dispara el refresco en caliente de toda la UI.</summary>
    public CultureInfo CurrentCulture
    {
        get => _currentCulture;
        set
        {
            if (Equals(_currentCulture, value))
            {
                return;
            }

            _currentCulture = value;
            CultureInfo.CurrentUICulture = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }
    }

    public bool IsSpanish => _currentCulture.TwoLetterISOLanguageName == "es";

    public void ToggleLanguage() =>
        CurrentCulture = IsSpanish ? CultureInfo.GetCultureInfo("en") : CultureInfo.GetCultureInfo("es");

    /// <summary>
    /// Indexador usado desde XAML como <c>{Binding [MainWindow_Title], Source={x:Static
    /// services:LocalizationService.Instance}}</c>: WPF permite bindear indexadores con
    /// esta sintaxis, y es lo que permite tener una sola propiedad "genérica" en vez de
    /// una por cada clave del .resx.
    /// </summary>
    public string this[string key] => _resourceManager.GetString(key, _currentCulture) ?? $"[[{key}]]";

    /// <summary>Para mensajes con parámetros (ej. "Conectado con \"{0}\"."), usado desde los ViewModels en C#.</summary>
    public string Format(string key, params object[] args) =>
        string.Format(_currentCulture, this[key], args);
}
