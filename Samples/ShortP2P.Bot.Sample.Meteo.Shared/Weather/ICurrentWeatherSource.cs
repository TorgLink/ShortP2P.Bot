namespace ShortP2P.Bot.Sample.Meteo.Weather;

public interface ICurrentWeatherSource
{
    Task<ForecastHit> CurrentAsync(double latitude, double longitude, CancellationToken cancellationToken);
}
