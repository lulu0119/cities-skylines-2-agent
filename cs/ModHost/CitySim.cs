using System;
using System.Text.Json;

namespace ModHost;

// Mock Cities: Skylines 2 world state.
// In the real mod this is replaced by ECS queries + native tool execution on the sim thread.
public sealed class CitySim
{
    private readonly CityState _state = new()
    {
        Population = 1200,
        Happiness = 62,
        Budget = 450_000,
        ResidentialDemand = "high",
        Roads = 8,
        ResidentialZones = 6,
        TaxRate = 11,
        Paused = true,
    };

    public string Overview() => JsonSerializer.Serialize(_state);

    public string BuildRoad()
    {
        _state.Roads += 1;
        _state.Budget -= 35_000;
        _state.ResidentialDemand = "medium";
        return Overview();
    }

    public string ZoneArea(string type, double x, double y, int size)
    {
        _ = type; _ = x; _ = y;
        _state.ResidentialZones += size;
        _state.Budget -= 12_000 * size;
        return Overview();
    }

    public string SetTaxRate(double rate)
    {
        _state.TaxRate = Math.Clamp(rate, 0, 30);
        _state.Happiness = Math.Max(0, _state.Happiness - (_state.TaxRate - 11) * 2);
        return Overview();
    }

    public string RunSimulation(int hours)
    {
        _ = hours;
        _state.Population += 180;
        _state.Happiness = Math.Min(100, _state.Happiness + 4);
        _state.Budget += 20_000;
        _state.ResidentialDemand = "low";
        return Overview();
    }

    private sealed class CityState
    {
        public int Population { get; set; }
        public double Happiness { get; set; }
        public long Budget { get; set; }
        public string ResidentialDemand { get; set; } = "low";
        public int Roads { get; set; }
        public int ResidentialZones { get; set; }
        public double TaxRate { get; set; }
        public bool Paused { get; set; }
    }
}
