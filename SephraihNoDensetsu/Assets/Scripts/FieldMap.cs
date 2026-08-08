// Controller for a non-linear multi-zone overworld region (e.g. a field scene made of several
// MapArea instances connected by SameSceneSubArea portals, possibly with more than one entrance).
// No field-specific behavior yet - MultiAreaMap's zone-activation, NavMesh rebake, stuck-rescue,
// and (crucially) spawnPointId-aware multi-entrance resolution already cover everything this needs.
// Kept as its own named class (rather than making MultiAreaMap directly attachable) to match the
// CityMap/DungeonMap precedent - a self-documenting Inspector component name, and an obvious place
// to add field-specific behavior later (weather, day/night, random encounters, etc.).
public class FieldMap : MultiAreaMap
{
}
