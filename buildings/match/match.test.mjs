// node --test buildings/match/   (no dependencies)
import test from 'node:test';
import assert from 'node:assert/strict';
import { classifyCs1, classifyCs2, matchAsset, matchAssets, indexCatalogue } from './match.mjs';

const cs2 = (name, components, extra = {}) => ({ name, kind: 'building', placeable: true, lot: [4, 4], components, flags: [], ...extra });
const catalogue = {
  prefabs: [
    cs2('Hospital01', { Hospital: { m_PatientCapacity: 500 } }, { capacity: { patients: 500 }, lot: [8, 8] }),
    cs2('MedicalClinic01', { Hospital: { m_PatientCapacity: 100 } }, { capacity: { patients: 100 }, lot: [4, 4] }),
    cs2('ElementarySchool01', { School: { m_EducationLevel: 1, m_StudentCapacity: 1000 } }, { capacity: { students: 1000 }, lot: [6, 6] }),
    cs2('HighSchool01', { School: { m_EducationLevel: 2, m_StudentCapacity: 1500 } }, { capacity: { students: 1500 }, lot: [8, 8] }),
    cs2('University01', { School: { m_EducationLevel: 4, m_StudentCapacity: 3000 } }, { capacity: { students: 3000 } }),
    cs2('SolarPowerStation01', { PowerPlant: { m_ElectricityProduction: 10000 }, SolarPowered: { m_Production: 1 } }, { capacity: { electricity: 10000 } }),
    cs2('CoalPowerStation01', { PowerPlant: { m_ElectricityProduction: 40000 } }, { capacity: { electricity: 40000 } }),
    cs2('Park01', { Park: {} }, { lot: [2, 2] }),
    cs2('Park02', { Park: {} }, { lot: [6, 6] }),
    cs2('ZonedHouse', { SpawnableBuilding: { m_Level: 1 } }, { placeable: false }),
    cs2('BusDepot01', { TransportDepot: { m_TransportType: 'Bus' } }),
    cs2('TaxiDepot01', { TransportDepot: { m_TransportType: 'Taxi' } }),
    cs2('Cemetery01', { DeathcareFacility: { m_LongTermStorage: true } }, { capacity: { bodies: 3000 } }),
    cs2('Crematorium01', { DeathcareFacility: { m_LongTermStorage: false } }, { capacity: { bodies: 100 } }),
  ],
};
const cs1 = (name, ai, extra = {}) => ({ name, ai, service: 'X', count: 1, footprint: [4, 4], aiFields: {}, tags: [], ...extra });

test('service building: same family, closest capacity wins', () => {
  const idx = indexCatalogue(catalogue);
  const clinic = matchAsset(cs1('Medical Clinic', 'HospitalAI', { aiFields: { m_patientCapacity: 100 } }), idx);
  assert.equal(clinic.decision, 'place');
  assert.equal(clinic.cs2, 'MedicalClinic01');
  const hospital = matchAsset(cs1('Hospital', 'HospitalAI', { footprint: [8, 8], aiFields: { m_patientCapacity: 450 } }), idx);
  assert.equal(hospital.cs2, 'Hospital01');
  assert.ok(hospital.score > 0.9, `score ${hospital.score}`);
  assert.match(hospital.reason, /patients 450 vs 500/);
});

test('schools keep their education level', () => {
  const idx = indexCatalogue(catalogue);
  assert.equal(matchAsset(cs1('Elementary School', 'SchoolAI', { level: 'Level1', aiFields: { m_studentCount: 300 } }), idx).cs2, 'ElementarySchool01');
  assert.equal(matchAsset(cs1('High School', 'SchoolAI', { level: 'Level2', aiFields: { m_studentCount: 1000 } }), idx).cs2, 'HighSchool01');
  assert.equal(matchAsset(cs1('University', 'SchoolAI', { level: 'Level3', aiFields: { m_studentCount: 4500 } }), idx).cs2, 'University01');
});

test('power plants keep their kind (solar stays solar even with a closer coal capacity)', () => {
  const idx = indexCatalogue(catalogue);
  const r = matchAsset(cs1('Solar Power Plant', 'SolarPowerPlantAI', { aiFields: { m_electricityProduction: 40000 } }), idx);
  assert.equal(r.cs2, 'SolarPowerStation01');
});

test('growables are not placed; infrastructure is skipped; no-equivalents are reported', () => {
  const idx = indexCatalogue(catalogue);
  assert.equal(matchAsset(cs1('L1 House', 'ResidentialBuildingAI', { growable: true, zone: 'ResidentialLow' }), idx).decision, 'growable');
  assert.equal(matchAsset(cs1('Electricity Pole', 'PowerPoleAI'), idx).decision, 'skip');
  assert.equal(matchAsset(cs1('HighwayRampPillar', 'BuildingAI'), idx).decision, 'skip');
  assert.equal(matchAsset(cs1('Rock', 'DecorationBuildingAI'), idx).decision, 'skip');
  const lib = matchAsset(cs1('Library 01', 'LibraryAI'), idx);
  assert.equal(lib.decision, 'noEquivalent');
  assert.match(lib.reason, /library/);
  assert.equal(matchAsset(cs1('Fire House', 'FireStationAI'), idx).decision, 'noEquivalent', 'no fire station in this catalogue');
});

test('parks match by lot size; depots by transport type; deathcare by kind', () => {
  const idx = indexCatalogue(catalogue);
  assert.equal(matchAsset(cs1('Big Park', 'ParkAI', { footprint: [6, 5] }), idx).cs2, 'Park02');
  assert.equal(matchAsset(cs1('Small Park', 'ParkAI', { footprint: [2, 2] }), idx).cs2, 'Park01');
  assert.equal(matchAsset(cs1('Taxi Depot', 'DepotAI'), idx).cs2, 'TaxiDepot01');
  assert.equal(matchAsset(cs1('Bus Depot', 'DepotAI'), idx).cs2, 'BusDepot01');
  assert.equal(matchAsset(cs1('Crematory', 'CemeteryAI', { aiFields: { m_corpseCapacity: 105 } }), idx).cs2, 'Crematorium01');
  assert.equal(matchAsset(cs1('Cemetery', 'CemeteryAI', { aiFields: { m_graveCount: 3000, m_corpseCapacity: 0 } }), idx).cs2, 'Cemetery01');
});

test('spawnable (zoned) CS2 prefabs are never candidates', () => {
  const idx = indexCatalogue(catalogue);
  for (const list of idx.byFamily.values()) for (const c of list) assert.notEqual(c.prefab.name, 'ZonedHouse');
});

test('matchAssets: stats per decision, deterministic', () => {
  const assets = { buildings: [
    cs1('Hospital', 'HospitalAI', { count: 2, aiFields: { m_patientCapacity: 500 } }),
    cs1('House', 'ResidentialBuildingAI', { growable: true, count: 300 }),
    cs1('Pole', 'PowerPoleAI', { count: 50 }),
  ] };
  const a = matchAssets(assets, catalogue);
  const b = matchAssets(assets, catalogue);
  assert.deepEqual(a, b);
  assert.equal(a.stats.byDecision.place.instances, 2);
  assert.equal(a.stats.byDecision.growable.instances, 300);
  assert.equal(a.stats.byDecision.skip.types, 1);
  assert.equal(a.byName.Hospital.cs2, 'Hospital01');
});

test('classifiers are total (never throw on sparse input)', () => {
  assert.doesNotThrow(() => classifyCs1({}));
  assert.doesNotThrow(() => classifyCs2({}));
});

test('slimCatalogue keeps exactly what matching needs', async () => {
  const { slimCatalogue } = await import('./match.mjs');
  const fat = { prefabs: [
    ...catalogue.prefabs.map((p) => ({ ...p, components: { ...p.components, Workplace: { m_Workplaces: 10 } }, flags: [...p.flags, 'EffectSource'] })),
    { name: 'Tree01', kind: 'object', placeable: true, components: {}, flags: [] },
  ] };
  const slim = slimCatalogue(fat);
  assert.ok(slim.prefabs.every((p) => p.kind === 'building' && p.placeable && !('Workplace' in p.components)));
  const assets = { buildings: [cs1('Hospital', 'HospitalAI', { aiFields: { m_patientCapacity: 450 } }), cs1('Big Park', 'ParkAI', { footprint: [6, 5] })] };
  assert.deepEqual(matchAssets(assets, slim).results, matchAssets(assets, fat).results);
});
