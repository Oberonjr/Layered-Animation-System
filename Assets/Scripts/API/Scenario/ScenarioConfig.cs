using System;
using System.Collections.Generic;

[Serializable]
public class ScenarioConfig
{
    public ScenarioInfo scenario;
    public CharacterInfo[] characters;
    public PlayerCharacterInfo player_character;
    public ProgressionStep[] required_progression_steps;
    public SystemInstructions system_instructions;
    public ResponseFormatExample example_response_format;
    public ConversationInitialization conversation_initialization;
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
    public string name;
    public string role;
    public string personality;
    public string background;
    public string communication_style;
    public string current_state;
    public string teaching_approach;
}

[Serializable]
public class PlayerCharacterInfo
{
    public string role;
    public string description;
    public string expectations;
}

[Serializable]
public class ProgressionStep
{
    public int step_id;
    public string title;
    public string description;
    public string[] key_points;
    public string[] teaching_moments;
}

[Serializable]
public class SystemInstructions
{
    public string[] core_rules;
    public string[] response_format;
    public string[] interaction_guidelines;
    public string[] teaching_behavior;
}

[Serializable]
public class ResponseFormatExample
{
    public int npc_index;
    public string dialogue;
    public string action;
    public string internal_thought;
}

[Serializable]
public class ConversationInitialization
{
    public int first_speaker_index;
    public string opening_prompt;
    public string context;
}
