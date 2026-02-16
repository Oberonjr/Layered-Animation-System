using System;
using System.Collections.Generic;

[Serializable]
public class ScenarioConfig
{
    public ScenarioInfo scenario;
    public CharacterInfo[] characters;
    public string initial_situation;
    public SystemInstructions system_instructions;
    public ResponseFormatExample example_response_format;
}

[Serializable]
public class ScenarioInfo
{
    public string title;
    public string description;
    public string setting;
    public string timeframe;
}

[Serializable]
public class CharacterInfo
{
    public string id;
    public string name;
    public string role;
    public string personality;
    public string background;
    public string communication_style;
    public string current_state;
    public string[] responsibilities;
}

[Serializable]
public class SystemInstructions
{
    public string[] core_rules;
    public string[] response_format;
    public string[] interaction_guidelines;
}

[Serializable]
public class ResponseFormatExample
{
    public string character_id;
    public string dialogue;
    public string action;
    public string internal_thought;
}

[Serializable]
public class NPCResponse
{
    public string character_id;
    public string dialogue;
    public string action;
    public string internal_thought;
}
