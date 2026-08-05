// Mock Cities: Skylines 2 world state.
// In the real mod this is replaced by ECS queries + native tool execution on the sim thread.

export interface CityOverview {
  population: number
  happiness: number
  budget: number
  residentialDemand: 'low' | 'medium' | 'high'
  roads: number
  residentialZones: number
  taxRate: number
  paused: boolean
}

export interface BuildRoadArgs {
  start: [number, number]
  end: [number, number]
}

export interface ZoneAreaArgs {
  type: 'residential' | 'commercial' | 'industrial'
  x: number
  y: number
  size: number
}

export class CitySim {
  private state: CityOverview = {
    population: 1200,
    happiness: 62,
    budget: 450_000,
    residentialDemand: 'high',
    roads: 8,
    residentialZones: 6,
    taxRate: 11,
    paused: true,
  }

  overview(): CityOverview {
    return { ...this.state }
  }

  buildRoad({ start, end }: BuildRoadArgs): CityOverview {
    this.state.roads += 1
    this.state.budget -= 35_000
    this.state.residentialDemand = 'medium'
    return this.overview()
  }

  zoneArea({ type, x, y, size }: ZoneAreaArgs): CityOverview {
    void type; void x; void y
    this.state.residentialZones += size
    this.state.budget -= 12_000 * size
    return this.overview()
  }

  setTaxRate(rate: number): CityOverview {
    this.state.taxRate = Math.max(0, Math.min(30, rate))
    this.state.happiness = Math.max(0, this.state.happiness - (this.state.taxRate - 11) * 2)
    return this.overview()
  }

  runSimulation(hours: number): CityOverview {
    void hours
    this.state.population += 180
    this.state.happiness = Math.min(100, this.state.happiness + 4)
    this.state.budget += 20_000
    this.state.residentialDemand = 'low'
    return this.overview()
  }
}
