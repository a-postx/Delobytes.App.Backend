namespace Delobytes.App.Backend.Constants;

public static class Timeouts
{
    public const int WebHostShutdownTimeoutSec = 120;
    public const int SqlCommandTimeoutSec = 60;
    public const int RuntimeGeoDetectionTimeoutSec = 10;
    public const int ApiRequestFilterMs = 60000;
}
