const arenaNames: Record<string, string> = {
  stadium_p: "DFH Stadium",
  stadium_foggy_p: "DFH Stadium (Stormy)",
  stadium_day_p: "DFH Stadium (Day)",
  stadium_winter_p: "DFH Stadium (Snowy)",
  stadium_race_day_p: "DFH Stadium (Circuit)",

  eurostadium_p: "Mannfield",
  eurostadium_rainy_p: "Mannfield (Stormy)",
  eurostadium_night_p: "Mannfield (Night)",
  eurostadium_snownight_p: "Mannfield (Snowy)",
  eurostadium_dusk_p: "Mannfield (Dusk)",

  cs_p: "Champions Field",
  cs_day_p: "Champions Field (Day)",
  bb_p: "Champions Field (NFL)",
  swoosh_p: "Champions Field (Nike FC)",
  cs_hw_p: "Rivals Arena",

  trainstation_p: "Urban Central",
  trainstation_night_p: "Urban Central (Night)",
  trainstation_dawn_p: "Urban Central (Dawn)",
  haunted_trainstation_p: "Urban Central (Haunted)",

  park_p: "Beckwith Park",
  park_rainy_p: "Beckwith Park (Stormy)",
  park_night_p: "Beckwith Park (Midnight)",
  park_snowy_p: "Beckwith Park (Snowy)",
  park_bman_p: "Beckwith Park (Gotham Night)",

  utopiastadium_p: "Utopia Coliseum",
  utopiastadium_dusk_p: "Utopia Coliseum (Dusk)",
  utopiastadium_snow_p: "Utopia Coliseum (Snowy)",
  utopiastadium_lux_p: "Utopia Coliseum (Gilded)",

  wasteland_s_p: "Wasteland",
  wasteland_night_s_p: "Wasteland (Night)",
  wasteland_grs_p: "Wasteland (Pitched)",
  wasteland_p: "Badlands",
  wasteland_night_p: "Badlands (Night)",

  neotokyo_standard_p: "Neo Tokyo",
  neotokyo_toon_p: "Neo Tokyo (Comic)",
  neotokyo_hax_p: "Neo Tokyo (Hacked)",
  neotokyo_p: "Tokyo Underpass",

  underwater_p: "AquaDome",

  arc_standard_p: "Starbase ARC",
  arc_darc_p: "Starbase ARC (Aftermath)",
  arc_p: "ARCtagon",

  farm_p: "Farmstead",
  farm_night_p: "Farmstead (Night)",
  farm_grs_p: "Farmstead (Pitched)",
  farm_hw_p: "Farmstead (The Upside Down)",

  beach_p: "Salty Shores",
  beach_night_p: "Salty Shores (Night)",
  beach_night_grs_p: "Salty Shores (Salty Fest)",

  chn_stadium_p: "Forbidden Temple",
  chn_stadium_day_p: "Forbidden Temple (Day)",
  fni_stadium_p: "Forbidden Temple (Fire & Ice)",

  outlaw_p: "Deadeye Canyon",
  outlaw_oasis_p: "Deadeye Canyon (Oasis)",

  music_p: "Neon Fields",

  street_p: "Sovereign Heights",

  ff_dusk_p: "Estadio Vida (Dusk)",

  throwbackstadium_p: "Throwback Stadium",
  throwbackhockey_p: "Throwback Stadium (Snowy)",

  hoopsstadium_p: "Dunk House",
  hoopsstreet_p: "The Block",

  shattershot_p: "Core 707",

  labs_circlepillars_p: "Pillars",
  labs_cosmic_v4_p: "Cosmic",
  labs_doublegoal_v2_p: "Double Goal",
  labs_octagon_02_p: "Octagon",
  labs_underpass_p: "Underpass",
  labs_utopia_p: "Utopia Retro",
  labs_basin_p: "Basin",
  labs_corridor_p: "Corridor",
  labs_holyfield_p: "Loophole",
  labs_galleon_p: "Galleon",
  labs_galleon_mast_p: "Galleon Retro",
  labs_pillarglass_p: "Hourglass",
  labs_pillarheat_p: "Barricade",
  labs_pillarwings_p: "Colossus",
};

export function getArenaDisplayName(code: string | null | undefined): string {
  if (code == null) {
    return "Unknown";
  }
  return arenaNames[code.toLowerCase()] ?? code;
}
