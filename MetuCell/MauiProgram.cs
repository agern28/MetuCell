using Microsoft.Extensions.Logging;
using MetuCell.Services;

namespace MetuCell
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder.UseMauiApp<App>().ConfigureFonts(fonts => {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });
            builder.Services.AddMauiBlazorWebView();

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif
            // KENDİ ŞİFRENİ YAZMAYI UNUTMA
            string connectionString = "Host=localhost;Port=5432;Database=MetuCellDB;Username=postgres;Password=123";
            builder.Services.AddSingleton(new DatabaseService(connectionString));
            builder.Services.AddSingleton<AppState>();

            return builder.Build();
        }
    }
}