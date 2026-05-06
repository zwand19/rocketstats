namespace RocketStats.Domain;

public static class ArenaNames
{
  private static readonly Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase)
  {
    // DFH Stadium
    ["Stadium_P"]           = "DFH Stadium",
    ["Stadium_Foggy_P"]     = "DFH Stadium (Stormy)",
    ["stadium_day_p"]       = "DFH Stadium (Day)",
    ["Stadium_Winter_P"]    = "DFH Stadium (Snowy)",
    ["Stadium_Race_Day_P"]  = "DFH Stadium (Circuit)",

    // Mannfield
    ["EuroStadium_P"]           = "Mannfield",
    ["EuroStadium_Rainy_P"]     = "Mannfield (Stormy)",
    ["EuroStadium_Night_P"]     = "Mannfield (Night)",
    ["eurostadium_snownight_p"] = "Mannfield (Snowy)",
    ["eurostadium_dusk_p"]      = "Mannfield (Dusk)",

    // Champions Field
    ["cs_p"]       = "Champions Field",
    ["cs_day_p"]   = "Champions Field (Day)",
    ["BB_P"]       = "Champions Field (NFL)",
    ["swoosh_p"]   = "Champions Field (Nike FC)",
    ["cs_hw_p"]    = "Rivals Arena",

    // Urban Central
    ["TrainStation_P"]         = "Urban Central",
    ["TrainStation_Night_P"]   = "Urban Central (Night)",
    ["TrainStation_Dawn_P"]    = "Urban Central (Dawn)",
    ["Haunted_TrainStation_P"] = "Urban Central (Haunted)",

    // Beckwith Park
    ["Park_P"]       = "Beckwith Park",
    ["Park_Rainy_P"] = "Beckwith Park (Stormy)",
    ["Park_Night_P"] = "Beckwith Park (Midnight)",
    ["Park_Snowy_P"] = "Beckwith Park (Snowy)",
    ["Park_Bman_P"]  = "Beckwith Park (Gotham Night)",

    // Utopia Coliseum
    ["UtopiaStadium_P"]      = "Utopia Coliseum",
    ["UtopiaStadium_Dusk_P"] = "Utopia Coliseum (Dusk)",
    ["UtopiaStadium_Snow_P"] = "Utopia Coliseum (Snowy)",
    ["UtopiaStadium_Lux_P"]  = "Utopia Coliseum (Gilded)",

    // Wasteland / Badlands
    ["wasteland_s_p"]      = "Wasteland",
    ["Wasteland_Night_S_P"]= "Wasteland (Night)",
    ["wasteland_grs_p"]    = "Wasteland (Pitched)",
    ["Wasteland_P"]        = "Badlands",
    ["Wasteland_Night_P"]  = "Badlands (Night)",

    // Neo Tokyo / Tokyo Underpass
    ["NeoTokyo_Standard_P"] = "Neo Tokyo",
    ["NeoTokyo_Toon_P"]     = "Neo Tokyo (Comic)",
    ["neotokyo_hax_p"]      = "Neo Tokyo (Hacked)",
    ["NeoTokyo_P"]          = "Tokyo Underpass",

    // AquaDome
    ["Underwater_P"] = "AquaDome",

    // Starbase ARC / ARCtagon
    ["arc_standard_p"] = "Starbase ARC",
    ["ARC_Darc_P"]     = "Starbase ARC (Aftermath)",
    ["ARC_P"]          = "ARCtagon",

    // Farmstead
    ["farm_p"]       = "Farmstead",
    ["Farm_Night_P"] = "Farmstead (Night)",
    ["farm_grs_p"]   = "Farmstead (Pitched)",
    ["Farm_HW_P"]    = "Farmstead (The Upside Down)",

    // Salty Shores
    ["beach_P"]           = "Salty Shores",
    ["beach_night_p"]     = "Salty Shores (Night)",
    ["Beach_Night_GRS_P"] = "Salty Shores (Salty Fest)",

    // Forbidden Temple
    ["CHN_Stadium_P"]     = "Forbidden Temple",
    ["CHN_Stadium_Day_P"] = "Forbidden Temple (Day)",
    ["fni_stadium_p"]     = "Forbidden Temple (Fire & Ice)",

    // Deadeye Canyon
    ["Outlaw_P"]        = "Deadeye Canyon",
    ["outlaw_oasis_p"]  = "Deadeye Canyon (Oasis)",

    // Neon Fields
    ["music_p"] = "Neon Fields",

    // Sovereign Heights
    ["Street_P"] = "Sovereign Heights",

    // Estadio Vida
    ["ff_dusk_p"] = "Estadio Vida (Dusk)",

    // Throwback Stadium
    ["ThrowbackStadium_P"] = "Throwback Stadium",
    ["ThrowbackHockey_p"]  = "Throwback Stadium (Snowy)",

    // Hoops
    ["HoopsStadium_P"] = "Dunk House",
    ["HoopsStreet_P"]  = "The Block",

    // Dropshot
    ["ShatterShot_P"] = "Core 707",

    // Rocket Labs
    ["Labs_CirclePillars_P"] = "Pillars",
    ["Labs_Cosmic_V4_P"]     = "Cosmic",
    ["Labs_DoubleGoal_V2_P"] = "Double Goal",
    ["Labs_Octagon_02_P"]    = "Octagon",
    ["Labs_Underpass_P"]     = "Underpass",
    ["Labs_Utopia_P"]        = "Utopia Retro",
    ["Labs_Basin_P"]         = "Basin",
    ["Labs_Corridor_P"]      = "Corridor",
    ["Labs_Holyfield_P"]     = "Loophole",
    ["Labs_Galleon_P"]       = "Galleon",
    ["Labs_Galleon_Mast_P"]  = "Galleon Retro",
    ["Labs_PillarGlass_P"]   = "Hourglass",
    ["Labs_PillarHeat_P"]    = "Barricade",
    ["Labs_PillarWings_P"]   = "Colossus",
  };

  public static string GetDisplayName(string? code) =>
    code is null ? "Unknown"
    : _map.TryGetValue(code, out var name) ? name
    : code;
}
