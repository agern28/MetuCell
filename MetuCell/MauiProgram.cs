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
#endif      //mobil
            //string connectionString = "Host=10.0.2.2;Port=5432;Database=MetuCellDB;Username=postgres;Password=123;SSL Mode=Disable;Trust Server Certificate=true";
            //desktop web
            string connectionString = "Host=localhost;Port=5432;Database=MetuCellDB;Username=postgres;Password=123";
            builder.Services.AddSingleton(new DatabaseService(connectionString));
            builder.Services.AddSingleton<AppState>();

            return builder.Build();
        }
    }
}