import { rawTool } from '@apeira/core'
import type { Tool } from '@apeira/core'

import { CitySim } from './citySim'

export const createCityTools = (city: CitySim): Tool[] => [
  rawTool({
    name: 'get_city_overview',
    description: '读取当前城市状态（人口、幸福感、预算、需求、道路、税率、暂停状态）。',
    parameters: {
      type: 'object',
      additionalProperties: false,
      properties: {},
      required: [],
    },
    execute: () => JSON.stringify(city.overview()),
  }),
  rawTool({
    name: 'build_road',
    description: '修建一条道路，会消耗预算并改善交通与需求。',
    parameters: {
      type: 'object',
      properties: {
        start: { type: 'array', items: { type: 'number' }, minItems: 2, maxItems: 2 },
        end: { type: 'array', items: { type: 'number' }, minItems: 2, maxItems: 2 },
      },
      required: ['start', 'end'],
      additionalProperties: false,
    },
    execute: (args) => JSON.stringify(city.buildRoad(args)),
  }),
  rawTool({
    name: 'zone_area',
    description: '规划一块区域（residential/commercial/industrial），会消耗预算并增加对应区划。',
    parameters: {
      type: 'object',
      properties: {
        type: { type: 'string', enum: ['residential', 'commercial', 'industrial'] },
        x: { type: 'number' },
        y: { type: 'number' },
        size: { type: 'number' },
      },
      required: ['type', 'x', 'y', 'size'],
      additionalProperties: false,
    },
    execute: (args) => JSON.stringify(city.zoneArea(args)),
  }),
  rawTool({
    name: 'set_tax_rate',
    description: '设置税率（0-30），提高税率增加收入但降低幸福感。',
    parameters: {
      type: 'object',
      properties: {
        rate: { type: 'number', minimum: 0, maximum: 30 },
      },
      required: ['rate'],
      additionalProperties: false,
    },
    execute: (args) => JSON.stringify(city.setTaxRate(args.rate)),
  }),
  rawTool({
    name: 'run_simulation',
    description: '把模拟向前推进指定游戏内小时数，观察城市变化。',
    parameters: {
      type: 'object',
      properties: {
        hours: { type: 'number', minimum: 1, maximum: 24 },
      },
      required: ['hours'],
      additionalProperties: false,
    },
    execute: (args) => JSON.stringify(city.runSimulation(args.hours)),
  }),
]
