#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace LAS
{
    // ═══════════════════════════════════════════════════════════════════
    //  Serializable data classes  (no Editor dependency)
    // ═══════════════════════════════════════════════════════════════════

    [Serializable]
    public class GeneratorLLMSettings
    {
        public float temperature    = 0.35f;
        public float topP           = 0.90f;
        public float topK           = 40f;
        public int   maxTokens      = 4096;
        public float repeatPenalty  = 1.0f;
        public int   timeoutSeconds = 120;

        public static GeneratorLLMSettings Defaults() => new GeneratorLLMSettings();
    }

    [Serializable]
    public class CharacterInputData
    {
        public bool   foldout            = true;
        public string name               = "";
        public string role               = "";
        public string personality        = "";
        public string background         = "";
        public string communicationStyle = "";
        public string currentState       = "";
        public string teachingApproach   = "";
    }

    [Serializable]
    public class ProgressionStepInputData
    {
        public bool         foldout         = true;
        public string       title           = "";
        public string       description     = "";
        public List<string> keyPoints       = new List<string>();
        public List<string> teachingMoments = new List<string>();
    }

    [Serializable]
    public class SysInstructionsInput
    {
        public List<string> coreRules             = new List<string>();
        public List<string> interactionGuidelines = new List<string>();
        public List<string> criticalRules         = new List<string>();
        public List<string> behaviorGuidelines    = new List<string>();
        public string       behaviorSectionLabel  = "";

        public static SysInstructionsInput Defaults() => new SysInstructionsInput
        {
            coreRules = new List<string>
            {
                "Stay completely in character at all times.",
                "Never mention AI, language models, simulations, or being a computer program."
            },
            criticalRules = new List<string>
            {
                "Never ask more than ONE question per response.",
                "Keep responses SHORT — 1 to 3 sentences maximum."
            },
            interactionGuidelines = new List<string>
            {
                "If the player addresses you directly, you respond.",
                "Avoid both characters responding to the same prompt at once."
            },
            behaviorGuidelines = new List<string>()
        };
    }

    [Serializable]
    public class ScenarioInputData
    {
        // ── Core (required) ───────────────────────────────
        public string idea = "";

        // ── Scenario info hints (all optional) ────────────
        public string scenarioTitle       = "";
        public string scenarioDescription = "";
        public string setting             = "";
        public string timeframe           = "";
        public string contextHint         = "";

        // ── Characters ────────────────────────────────────
        public List<CharacterInputData> characters = new List<CharacterInputData> { new CharacterInputData() };

        // ── Player character ──────────────────────────────
        public bool   includePlayer      = true;
        public string playerRole         = "";
        public string playerDescription  = "";
        public string playerExpectations = "";

        // ── Progression steps ─────────────────────────────
        public bool                           includeProgression = true;
        public List<ProgressionStepInputData> progressionSteps   = new List<ProgressionStepInputData>();

        // ── System instructions ───────────────────────────
        public bool               includeSysInstructions = true;
        public SysInstructionsInput sysInstructions      = SysInstructionsInput.Defaults();

        // ── Conversation init ─────────────────────────────
        public bool   includeConvInit       = true;
        public int    firstSpeakerIndex     = 0;
        public string openingPrompt         = "";
        public string conversationContext   = "";
        public string npcConversationPrompt = "";
        public string idlePlayerPrompt      = "";

        // ── Extra ─────────────────────────────────────────
        public string additionalNotes = "";

        // ── Output ────────────────────────────────────────
        public string filename     = "";
        public string outputFolder = "Assets/JSON/ScenarioConfigs/CustomScenarios";

        public static ScenarioInputData Defaults() => new ScenarioInputData();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  LLM response wire types
    // ═══════════════════════════════════════════════════════════════════

    [Serializable] internal class AnthropicResp  { public AnthropicBlock[] content; }
    [Serializable] internal class AnthropicBlock { public string type; public string text; }
    [Serializable] internal class OpenAIResp     { public OpenAIChoice[] choices; }
    [Serializable] internal class OpenAIChoice   { public OpenAIMsg message; }
    [Serializable] internal class OpenAIMsg      { public string content; }
    [Serializable] internal class OllamaResp     { public OllamaMsg message; }
    [Serializable] internal class OllamaMsg      { public string content; }

    // ═══════════════════════════════════════════════════════════════════
    //  Editor Window
    // ═══════════════════════════════════════════════════════════════════

    public class ScenarioGeneratorWindow : EditorWindow
    {
        // ── Persistent state ──────────────────────────────────────────
        private LLMProviderBase      _provider;
        private GeneratorLLMSettings _llm      = new GeneratorLLMSettings();
        private ScenarioInputData    _scenario  = ScenarioInputData.Defaults();
        private string               _basePrompt = "";
        private TextAsset            _promptFile;

        // ── UI state ──────────────────────────────────────────────────
        private Vector2 _scroll;
        private bool _foldLLM      = true;
        private bool _foldScenario = true;
        private bool _foldOutput   = true;
        private bool _foldPrompt   = false;
        private bool _foldValidate = false;
        private bool _foldSysInst  = false;
        private bool _foldConvInit = false;

        // Scroll positions for large text areas
        private Vector2 _ideaScroll;
        private Vector2 _notesScroll;
        private Vector2 _promptScroll;

        // ── Generation state ──────────────────────────────────────────
        private bool   _isGenerating;
        private float  _progress;
        private double _genStartTime;
        private string _statusMsg  = "";
        private string _errorMsg   = "";
        private string _successMsg = "";

        // ── Validation / import ───────────────────────────────────────
        private TextAsset _validateFile;
        private string    _validateResult  = "";
        private bool      _validateSuccess = true;

        // ── Styles ────────────────────────────────────────────────────
        private bool     _stylesReady;
        private GUIStyle _titleStyle;
        private GUIStyle _descStyle;

        // ── EditorPrefs keys ──────────────────────────────────────────
        private const string K_PROVIDER = "LAS_SG_Provider";
        private const string K_LLM      = "LAS_SG_LLM";
        private const string K_SCENE    = "LAS_SG_Scene";
        private const string K_PROMPT   = "LAS_SG_Prompt";

        // ═════════════════════════════════════════════════════════════
        //  Menu
        // ═════════════════════════════════════════════════════════════

        [MenuItem("LAS/Scenario Generator", priority = 200)]
        public static void Open()
        {
            var w = GetWindow<ScenarioGeneratorWindow>("Scenario Generator");
            w.minSize = new Vector2(480, 600);
        }

        // ═════════════════════════════════════════════════════════════
        //  Lifecycle
        // ═════════════════════════════════════════════════════════════

        private void OnEnable()
        {
            LoadSettings();
            EditorApplication.update += Tick;
        }

        private void OnDisable()
        {
            SaveSettings();
            EditorApplication.update -= Tick;
        }

        private void Tick()
        {
            if (!_isGenerating) return;
            float elapsed = (float)(EditorApplication.timeSinceStartup - _genStartTime);
            _progress = 1f - Mathf.Exp(-elapsed / 25f);
            Repaint();
        }

        // ═════════════════════════════════════════════════════════════
        //  OnGUI
        // ═════════════════════════════════════════════════════════════

        private void OnGUI()
        {
            EnsureStyles();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawHeader();
            Space(6);

            DrawSection(ref _foldLLM,      "  LLM PROVIDER SETTINGS",        DrawLLMContent);
            Space(2);
            DrawSection(ref _foldScenario, "  SCENARIO CONTENT",              DrawScenarioContent);
            Space(2);
            DrawSection(ref _foldOutput,   "  OUTPUT SETTINGS",               DrawOutputContent);
            Space(2);
            DrawSection(ref _foldPrompt,   "  GENERATION PROMPT  (Advanced)", DrawPromptContent);
            Space(2);
            DrawSection(ref _foldValidate, "  VALIDATE / IMPORT JSON",        DrawValidateContent);
            Space(8);

            DrawStatus();
            Space(8);

            using (new EditorGUI.DisabledScope(_isGenerating))
            {
                var prev = GUI.backgroundColor;
                GUI.backgroundColor = _isGenerating ? Color.grey : new Color(0.25f, 0.75f, 0.35f);
                if (GUILayout.Button(_isGenerating ? "  Generating…  " : "  GENERATE SCENARIO JSON  ", GUILayout.Height(34)))
                    TryGenerate();
                GUI.backgroundColor = prev;
            }
            Space(8);

            EditorGUILayout.EndScrollView();
        }

        // ═════════════════════════════════════════════════════════════
        //  Header
        // ═════════════════════════════════════════════════════════════

        private void DrawHeader()
        {
            Space(6);
            EditorGUILayout.LabelField("SCENARIO GENERATOR", _titleStyle);
            EditorGUILayout.LabelField(
                "Describe your scenario idea, fill in the details you want, and let an AI write the complete scenario JSON for you.",
                EditorStyles.wordWrappedMiniLabel);
            Space(6);
        }

        // ═════════════════════════════════════════════════════════════
        //  Section: LLM Settings
        // ═════════════════════════════════════════════════════════════

        private void DrawLLMContent()
        {
            Note("Configure which AI model writes your scenario and how it generates. Once set, you rarely need to come back here.");
            Space(4);

            // ── Provider asset ────────────────────────────────────────
            Bold("PROVIDER ASSET");
            Note("Drag one of your existing LLMProviderBase ScriptableObjects here (ClaudeProvider, OpenAIChatProvider, OllamaProvider). " +
                 "The generator reads the model name and API key directly from that asset — no duplicate configuration needed.");

            var newProvider = (LLMProviderBase)EditorGUILayout.ObjectField(
                new GUIContent("Provider Asset",
                    "The LLMProviderBase ScriptableObject to use for generation. " +
                    "Drag a ClaudeProvider, OpenAIChatProvider, or OllamaProvider from your Project window here."),
                _provider, typeof(LLMProviderBase), false);

            if (newProvider != _provider)
            {
                _provider = newProvider;
                SaveSettings();
            }

            Space(4);

            if (_provider == null)
            {
                EditorGUILayout.HelpBox(
                    "No provider assigned.\n\n" +
                    "Drag a ClaudeProvider, OpenAIChatProvider, or OllamaProvider ScriptableObject " +
                    "from your Project window into the slot above.",
                    UnityEditor.MessageType.Warning);
            }
            else
            {
                DrawProviderInfoBox();
            }

            Space(8);

            // ── Generation parameters ─────────────────────────────────
            Bold("GENERATION PARAMETERS");
            Note("These control HOW the AI writes. For JSON generation, keep Temperature low (0.2–0.5). Other values can usually stay at their defaults.");

            Field("Temperature  [0 – 2]",
                "Controls how creative or random the AI's word choices are.\n" +
                "  0 = very predictable and rigid\n" +
                "  1 = balanced creativity\n" +
                "  2 = highly unpredictable\n" +
                "Keep at 0.2–0.5 for JSON generation. Higher values can produce malformed JSON.",
                () => _llm.temperature = EditorGUILayout.Slider(_llm.temperature, 0f, 2f));

            Field("Top P  [0 – 1]",
                "Limits word choice to the most probable options. Lower = more focused vocabulary. " +
                "0.9 is a reliable default. Only lower this if you get very repetitive or looping outputs.",
                () => _llm.topP = EditorGUILayout.Slider(_llm.topP, 0.01f, 1f));

            Field("Top K  (Ollama only)  [1 – 100]",
                "How many word candidates the AI considers at each step. Only used by Ollama — ignored by all cloud providers. " +
                "40 is a sensible default for structured JSON output.",
                () => _llm.topK = EditorGUILayout.Slider(_llm.topK, 1f, 100f));

            Field("Max Output Tokens",
                "The maximum number of tokens (roughly 4 characters each) the AI can output in one response. " +
                "A full scenario with 5 progression steps is typically 1500–2500 tokens. " +
                "Keep this at 3000+ to avoid the output being cut off mid-JSON.",
                () => _llm.maxTokens = EditorGUILayout.IntField(_llm.maxTokens));

            Field("Repeat Penalty  (Ollama only)  [1.0 – 2.0]",
                "Discourages the AI from repeating the same words or phrases. Only used by Ollama. " +
                "1.0 = no penalty. Raise to 1.1–1.2 if you see looping or repeated phrases in the output.",
                () => _llm.repeatPenalty = EditorGUILayout.Slider(_llm.repeatPenalty, 1f, 2f));

            Field("Request Timeout (seconds)",
                "How long to wait for the AI before giving up with a timeout error. " +
                "Large cloud models and complex scenarios may need 90–180 seconds. " +
                "Increase this if you keep seeing timeout errors.",
                () => _llm.timeoutSeconds = EditorGUILayout.IntField(_llm.timeoutSeconds));

            Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open API Key File"))
                {
                    ApiKeyStore.EnsureFileExists();
                    EditorUtility.RevealInFinder(ApiKeyStore.KeyFilePath);
                }
                if (GUILayout.Button("Reset Parameters to Defaults"))
                {
                    if (Confirm("Reset Parameters", "Reset all generation parameters to their default values?"))
                        _llm = GeneratorLLMSettings.Defaults();
                }
            }
        }

        private void DrawProviderInfoBox()
        {
            bool isOllama = _provider is OllamaProvider;
            string model   = ProviderModelName();
            string keyName = isOllama ? "" : ProviderApiKeyName();
            bool   keyOk   = isOllama || ApiKeyStore.HasKey(keyName);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(_provider.ProviderDisplayName, EditorStyles.boldLabel);

                EditorGUILayout.LabelField(
                    string.IsNullOrEmpty(model)
                        ? "Model:  (not set — open the provider asset to configure it)"
                        : $"Model:  {model}",
                    EditorStyles.miniLabel);

                if (!isOllama)
                {
                    var prev = GUI.color;
                    GUI.color = keyOk ? new Color(0.4f, 1f, 0.5f) : new Color(1f, 0.4f, 0.4f);
                    EditorGUILayout.LabelField(
                        keyOk
                            ? $"API Key:  {keyName}  ✓  key found"
                            : $"API Key:  {keyName}  ✗  NOT FOUND — open key file",
                        EditorStyles.miniLabel);
                    GUI.color = prev;
                }
                else
                {
                    EditorGUILayout.LabelField($"URL:  {ProviderOllamaUrl()}", EditorStyles.miniLabel);
                }
            }
        }

        // ═════════════════════════════════════════════════════════════
        //  Section: Scenario Content
        // ═════════════════════════════════════════════════════════════

        private void DrawScenarioContent()
        {
            Note("Fill in your idea and any details you care about. Fields marked ★ are required. All other fields are optional — the AI invents them if left blank.");
            Space(4);

            // ── Concept ──────────────────────────────────────────────
            Bold("★  CONCEPT / IDEA  (Required)");
            Note("Describe your scenario in plain language. The more detail here, the closer the output will match your vision. " +
                 "Example: 'A junior nurse learning sterile gloving from a senior nurse and a supervising doctor in a hospital prep room, using the Socratic method.'");
            _ideaScroll = EditorGUILayout.BeginScrollView(_ideaScroll, GUILayout.Height(92));
            _scenario.idea = EditorGUILayout.TextArea(_scenario.idea, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            Space(8);

            // ── Scenario info hints ───────────────────────────────────
            Bold("SCENARIO DETAILS  (Optional — AI fills if blank)");

            Field("Title",
                "A short name for the scenario shown in menus and to the player at start. If blank, the AI creates one from your concept.",
                () => _scenario.scenarioTitle = EditorGUILayout.TextField(_scenario.scenarioTitle));

            Field("Setting",
                "Where the scenario physically takes place. E.g. 'Hospital operating room — preparation phase'. If blank, the AI infers this from your concept.",
                () => _scenario.setting = EditorGUILayout.TextField(_scenario.setting));

            Field("Timeframe",
                "When the scenario takes place relative to some event. E.g. '30 minutes before the patient arrives', 'First day on the job'. " +
                "Gives the AI temporal context for NPC dialogue.",
                () => _scenario.timeframe = EditorGUILayout.TextField(_scenario.timeframe));

            Field("Description",
                "A 1-2 sentence summary shown to the player at scenario start. If blank, the AI writes one.",
                () => _scenario.scenarioDescription = EditorGUILayout.TextField(_scenario.scenarioDescription));

            Field("Context Hint",
                "A very short phrase (3–8 words) used internally by the AI alias generator when naming objects. " +
                "E.g. 'hospital prep room training session'. Falls back to the title if left blank.",
                () => _scenario.contextHint = EditorGUILayout.TextField(_scenario.contextHint));

            Space(8);

            DrawCharactersSection();
            Space(6);
            DrawPlayerSection();
            Space(6);
            DrawProgressionSection();
            Space(6);

            // ── System instructions ───────────────────────────────────
            _foldSysInst = EditorGUILayout.Foldout(_foldSysInst, "System Instructions  (Optional — AI generates if all lists are empty)");
            if (_foldSysInst)
            {
                EditorGUI.indentLevel++;
                Note("These rules go directly into each NPC's AI prompt. Leave the lists empty and the AI writes sensible defaults for your scenario. " +
                     "Fill them in only if you need precise control over NPC behavior.");

                _scenario.includeSysInstructions = EditorGUILayout.ToggleLeft(
                    new GUIContent("Include System Instructions",
                        "Whether to include a system_instructions block in the JSON at all. " +
                        "Disabling this makes NPCs rely entirely on the fallback rules set in the NPCManager inspector."),
                    _scenario.includeSysInstructions);

                if (_scenario.includeSysInstructions)
                {
                    Space(4);
                    Field("Behavior Section Label",
                        "The heading shown in the NPC prompt for the behavior guidelines block. " +
                        "Default is 'BEHAVIOR GUIDELINES'. Use 'TEACHING BEHAVIOR' for training scenarios, " +
                        "'ROLE-PLAY GUIDELINES' for improv-style scenarios, etc.",
                        () => _scenario.sysInstructions.behaviorSectionLabel = EditorGUILayout.TextField(_scenario.sysInstructions.behaviorSectionLabel));

                    ListField("Core Rules",
                        "Absolute rules the NPC must always follow. Injected under 'CORE RULES (FOLLOW STRICTLY)'. " +
                        "E.g. 'Never break character', 'Always acknowledge what the player says'. AI generates a full set if this list is empty.",
                        _scenario.sysInstructions.coreRules);

                    ListField("Interaction Guidelines",
                        "Rules about how NPCs interact with each other and with the player. " +
                        "E.g. 'Only one NPC responds per turn unless it is a genuine team discussion'. AI generates if empty.",
                        _scenario.sysInstructions.interactionGuidelines);

                    ListField("Critical Rules",
                        "High-priority conversational rules. E.g. 'Never ask more than one question per response'. " +
                        "If empty, the NPCManager inspector fallback critical-rules field is used instead.",
                        _scenario.sysInstructions.criticalRules);

                    ListField("Behavior Guidelines",
                        "Domain-specific behavior guidelines. E.g. teaching methodology, tone, how to handle mistakes. " +
                        "If empty, the NPCManager inspector behavior-guidelines field is used as fallback.",
                        _scenario.sysInstructions.behaviorGuidelines);
                }
                EditorGUI.indentLevel--;
            }

            Space(6);

            // ── Conversation init ─────────────────────────────────────
            _foldConvInit = EditorGUILayout.Foldout(_foldConvInit, "Conversation Initialization  (Optional — AI fills if blank)");
            if (_foldConvInit)
            {
                EditorGUI.indentLevel++;
                Note("Controls how the scenario starts. If left empty, the AI writes a sensible opening based on your concept.");

                _scenario.includeConvInit = EditorGUILayout.ToggleLeft(
                    new GUIContent("Include Conversation Init",
                        "Whether to include conversation_initialization in the JSON. " +
                        "Disabling this means the scenario will not auto-start a conversation — you must trigger it manually via the NPCManager API."),
                    _scenario.includeConvInit);

                if (_scenario.includeConvInit)
                {
                    Space(4);
                    Field("First Speaker Index",
                        "Which NPC speaks first when the scenario begins. 0 = the first character in your list, 1 = the second, etc.",
                        () => _scenario.firstSpeakerIndex = EditorGUILayout.IntField(_scenario.firstSpeakerIndex));

                    Field("Opening Prompt",
                        "The instruction sent to the first NPC to kick off the conversation. " +
                        "E.g. 'Dr. Chen welcomes the student and begins the pre-operative preparation session.' AI writes one if blank.",
                        () => _scenario.openingPrompt = EditorGUILayout.TextField(_scenario.openingPrompt));

                    Field("Scene Context",
                        "A brief description of what is happening in the scene at the moment the scenario opens. " +
                        "E.g. 'The surgical team has just entered the operating room. The patient has not yet arrived.'",
                        () => _scenario.conversationContext = EditorGUILayout.TextField(_scenario.conversationContext));

                    Field("NPC Auto-Conversation Prompt",
                        "What the NPCs are told when continuing a conversation amongst themselves without waiting for the player. " +
                        "Falls back to the NPCManager inspector npc_conversation_prompt field if left blank.",
                        () => _scenario.npcConversationPrompt = EditorGUILayout.TextField(_scenario.npcConversationPrompt));

                    Field("Idle Player Prompt",
                        "What the NPCs are told when the player has been quiet for a while and one of them should check in. " +
                        "Falls back to the NPCManager inspector idle_player_prompt field if left blank.",
                        () => _scenario.idlePlayerPrompt = EditorGUILayout.TextField(_scenario.idlePlayerPrompt));
                }
                EditorGUI.indentLevel--;
            }

            Space(8);

            // ── Additional notes ──────────────────────────────────────
            Bold("ADDITIONAL DESIGNER NOTES  (Optional)");
            Note("Any extra instructions, constraints, or preferences for the AI. Free-form text — write whatever would help. " +
                 "Examples: 'Keep dialogue tone very formal', 'Do not include more than 4 progression steps', 'The nurse should be slightly nervous'.");
            _notesScroll = EditorGUILayout.BeginScrollView(_notesScroll, GUILayout.Height(74));
            _scenario.additionalNotes = EditorGUILayout.TextArea(_scenario.additionalNotes, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            Space(8);

            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
            if (GUILayout.Button("  Reset All Scenario Rules & Content  "))
            {
                if (Confirm("Reset Scenario Content", "Clear ALL scenario fields and start fresh?"))
                    _scenario = ScenarioInputData.Defaults();
            }
            GUI.backgroundColor = prevBg;
        }

        private void DrawCharactersSection()
        {
            Bold("★  CHARACTERS  (At least 1 Required)");
            Note("Define the NPC characters. Only Name and Role are required per character — the AI invents everything else. " +
                 "The number of characters here must match the number of NPCController components registered with NPCManager in your scene.");

            for (int i = 0; i < _scenario.characters.Count; i++)
            {
                var c = _scenario.characters[i];
                using (new EditorGUILayout.VerticalScope("box"))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        c.foldout = EditorGUILayout.Foldout(c.foldout,
                            $"Character {i + 1}{(string.IsNullOrEmpty(c.name) ? "" : $"  —  {c.name}")}");
                        GUILayout.FlexibleSpace();
                        if (_scenario.characters.Count > 1)
                        {
                            var prev2 = GUI.backgroundColor;
                            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                            if (GUILayout.Button("✕", GUILayout.Width(24)))
                            { _scenario.characters.RemoveAt(i); GUI.backgroundColor = prev2; break; }
                            GUI.backgroundColor = prev2;
                        }
                    }

                    if (!c.foldout) continue;

                    EditorGUI.indentLevel++;

                    Field("★ Name  (Required)",
                        "The character's full name. E.g. 'Dr. Sarah Chen'. Used in dialogue and as the NPC's identity throughout the system.",
                        () => c.name = EditorGUILayout.TextField(c.name));

                    Field("★ Role  (Required)",
                        "Their job title or position in the scenario. E.g. 'Lead Surgeon', 'Senior Nurse', 'Head Barista'. " +
                        "Shown to the player and used in all NPC prompts.",
                        () => c.role = EditorGUILayout.TextField(c.role));

                    Field("Personality",
                        "Personality traits and general disposition. E.g. 'Patient, methodical, and encouraging. Takes teaching seriously.' " +
                        "AI generates from your concept if blank.",
                        () => c.personality = EditorGUILayout.TextField(c.personality));

                    Field("Background",
                        "Professional history and relevant expertise. E.g. '15 years of surgical experience, passionate about mentoring students.' " +
                        "AI generates if blank.",
                        () => c.background = EditorGUILayout.TextField(c.background));

                    Field("Communication Style",
                        "How this character speaks and interacts. E.g. 'Clear and instructional — asks focused questions, gives concise feedback.' " +
                        "AI generates if blank.",
                        () => c.communicationStyle = EditorGUILayout.TextField(c.communicationStyle));

                    Field("Current State",
                        "What this character is physically doing at the very start of the scenario. " +
                        "E.g. 'Reviewing the procedure checklist and preparing to guide the student.' AI generates if blank.",
                        () => c.currentState = EditorGUILayout.TextField(c.currentState));

                    Field("Teaching Approach  (Optional)",
                        "How this character specifically approaches teaching, if relevant. " +
                        "E.g. 'Socratic method — asks guiding questions before explaining, encourages the student to reason through steps.' " +
                        "Leave blank for non-teaching scenarios or to let the AI decide.",
                        () => c.teachingApproach = EditorGUILayout.TextField(c.teachingApproach));

                    EditorGUI.indentLevel--;
                }
            }

            if (GUILayout.Button("+ Add Character"))
                _scenario.characters.Add(new CharacterInputData());
        }

        private void DrawPlayerSection()
        {
            _scenario.includePlayer = EditorGUILayout.ToggleLeft(
                new GUIContent("Include Player Character",
                    "Whether to generate a player_character block in the JSON. " +
                    "Enable this to give the player a defined role, description, and set of expectations shown at scenario start. " +
                    "Disable for scenarios where the player observes without a defined character."),
                _scenario.includePlayer);

            if (!_scenario.includePlayer) return;

            Space(2);
            using (new EditorGUILayout.VerticalScope("box"))
            {
                Bold("PLAYER CHARACTER  (Optional details — AI fills if blank)");
                EditorGUI.indentLevel++;

                Field("Player Role",
                    "The player's job title or role in the scenario. E.g. 'Final-Year Medical Student', 'New Hire Barista'. " +
                    "Shown to the player at scenario start and referenced in NPC prompts. AI generates if blank.",
                    () => _scenario.playerRole = EditorGUILayout.TextField(_scenario.playerRole));

                Field("Player Description",
                    "A 1-2 sentence description of who the player is, shown at scenario start. " +
                    "E.g. 'You are a final-year medical student on your surgical rotation.' AI generates if blank.",
                    () => _scenario.playerDescription = EditorGUILayout.TextField(_scenario.playerDescription));

                Field("Player Expectations",
                    "What the player is expected to do during the scenario. " +
                    "E.g. 'Follow instructions, ask questions when unsure, and demonstrate procedural understanding.' AI generates if blank.",
                    () => _scenario.playerExpectations = EditorGUILayout.TextField(_scenario.playerExpectations));

                EditorGUI.indentLevel--;
            }
        }

        private void DrawProgressionSection()
        {
            _scenario.includeProgression = EditorGUILayout.ToggleLeft(
                new GUIContent("Include Progression Steps",
                    "Whether to generate required_progression_steps in the JSON. " +
                    "Progression steps define the ordered phases of the scenario (e.g. Briefing → Setup → Verification). " +
                    "NPCManager tracks which step is active and advances through them. " +
                    "Disable this for free-form scenarios with no structured sequence."),
                _scenario.includeProgression);

            if (!_scenario.includeProgression) return;

            Space(2);
            Bold("PROGRESSION STEPS  (Optional detail — AI generates structure if steps are empty)");
            Note("Define the ordered phases of your scenario. Add empty steps with a count — the AI fills in the details. " +
                 "Or provide titles and descriptions for tighter control. Typical scenarios have 3–6 steps.");

            for (int i = 0; i < _scenario.progressionSteps.Count; i++)
            {
                var s = _scenario.progressionSteps[i];
                using (new EditorGUILayout.VerticalScope("box"))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        s.foldout = EditorGUILayout.Foldout(s.foldout,
                            $"Step {i + 1}{(string.IsNullOrEmpty(s.title) ? "" : $"  —  {s.title}")}");
                        GUILayout.FlexibleSpace();
                        var prev2 = GUI.backgroundColor;
                        GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                        if (GUILayout.Button("✕", GUILayout.Width(24)))
                        { _scenario.progressionSteps.RemoveAt(i); GUI.backgroundColor = prev2; break; }
                        GUI.backgroundColor = prev2;
                    }

                    if (!s.foldout) continue;

                    EditorGUI.indentLevel++;

                    Field("Title",
                        "Short name for this phase. E.g. 'Initial Briefing', 'Sterile Setup'. Shown in debug logs. AI fills if blank.",
                        () => s.title = EditorGUILayout.TextField(s.title));

                    Field("Description",
                        "1-2 sentences describing what happens during this phase. AI fills if blank.",
                        () => s.description = EditorGUILayout.TextField(s.description));

                    ListField("Key Points",
                        "2–4 short facts or actions that define this step. E.g. 'Surgical light activation'. AI generates if empty.",
                        s.keyPoints);

                    ListField("Teaching Moments",
                        "2–3 specific opportunities to teach the player during this step. AI generates if empty.",
                        s.teachingMoments);

                    EditorGUI.indentLevel--;
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+ Add Step"))
                    _scenario.progressionSteps.Add(new ProgressionStepInputData());
                if (_scenario.progressionSteps.Count == 0 && GUILayout.Button("Add 4 blank steps  (AI fills details)"))
                    for (int k = 0; k < 4; k++)
                        _scenario.progressionSteps.Add(new ProgressionStepInputData());
            }
        }

        // ═════════════════════════════════════════════════════════════
        //  Section: Output Settings
        // ═════════════════════════════════════════════════════════════

        private void DrawOutputContent()
        {
            Field("Filename  (without .json)",
                "Custom name for the generated JSON file. Leave blank to use a default name with the date and time appended " +
                "(e.g. scenario_2026-06-11_14-30-00.json). " +
                "If the name matches an existing file in the output folder, _V2, _V3, etc. is appended automatically — your existing files are never overwritten.",
                () => _scenario.filename = EditorGUILayout.TextField(_scenario.filename));

            Field("Output Folder",
                "The folder where generated scenario files are saved. Use a path relative to the project root starting with Assets/. " +
                "The folder is created automatically if it does not exist. Default: Assets/JSON/ScenarioConfigs/CustomScenarios",
                () => _scenario.outputFolder = EditorGUILayout.TextField(_scenario.outputFolder));

            Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open Output Folder"))
                {
                    string abs = ToAbsPath(_scenario.outputFolder);
                    Directory.CreateDirectory(abs);
                    EditorUtility.RevealInFinder(abs);
                }
                if (GUILayout.Button("Browse…"))
                {
                    string chosen = EditorUtility.OpenFolderPanel("Choose output folder", Application.dataPath, "");
                    if (!string.IsNullOrEmpty(chosen))
                    {
                        string dataRoot = Application.dataPath.Replace("\\", "/").TrimEnd('/');
                        string abs2     = chosen.Replace("\\", "/").TrimEnd('/');
                        _scenario.outputFolder = abs2.StartsWith(dataRoot)
                            ? "Assets" + abs2.Substring(dataRoot.Length)
                            : abs2;
                    }
                }
            }
        }

        // ═════════════════════════════════════════════════════════════
        //  Section: Generation Prompt
        // ═════════════════════════════════════════════════════════════

        private void DrawPromptContent()
        {
            Note("The system prompt sent to the AI telling it exactly how to write your scenario JSON. " +
                 "Most users never need to touch this — it is exposed for advanced designers who want to tweak output style or add schema constraints.");
            Space(4);

            Field("Prompt from .txt File  (overrides text below)",
                "Optional: assign a TextAsset (.txt file) from your Project window here. If assigned, the text field below is ignored " +
                "and the file content is used as the system prompt instead. Useful for version-controlling your prompt separately.",
                () => _promptFile = (TextAsset)EditorGUILayout.ObjectField(_promptFile, typeof(TextAsset), false));

            Space(4);

            if (_promptFile != null)
            {
                EditorGUILayout.HelpBox($"Using prompt from: {AssetDatabase.GetAssetPath(_promptFile)}", UnityEditor.MessageType.Info);
            }
            else
            {
                EditorGUILayout.LabelField(new GUIContent("System Prompt  (editable below)",
                    "The instructions sent to the AI before your scenario concept. Edit to add extra constraints, " +
                    "change the output style, or adjust the JSON schema reference."), EditorStyles.boldLabel);
                _promptScroll = EditorGUILayout.BeginScrollView(_promptScroll, GUILayout.Height(150));
                _basePrompt = EditorGUILayout.TextArea(_basePrompt, GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();
            }

            Space(4);
            if (GUILayout.Button("Reset to Default Prompt"))
            {
                if (Confirm("Reset Prompt", "Replace the current prompt with the built-in default?"))
                { _basePrompt = DefaultPrompt(); _promptFile = null; }
            }
        }

        // ═════════════════════════════════════════════════════════════
        //  Section: Validate / Import
        // ═════════════════════════════════════════════════════════════

        private void DrawValidateContent()
        {
            Note("Use this to check an existing scenario JSON file, or to pull its data into the fields above for quick iteration.");
            Space(4);

            Field("Scenario JSON File",
                "Assign a .json TextAsset from your project here (drag from Project window or use the picker). " +
                "Then use the buttons below:\n" +
                "  Validate JSON — checks that the file is correctly structured and all required fields are present.\n" +
                "  Extract Data to Fields — reads the file and populates all the fields above so you can edit and regenerate.",
                () => _validateFile = (TextAsset)EditorGUILayout.ObjectField(_validateFile, typeof(TextAsset), false));

            Rect dropZone = GUILayoutUtility.GetRect(0, 36, GUILayout.ExpandWidth(true));
            GUI.Box(dropZone, "— or drag a JSON TextAsset here —", EditorStyles.helpBox);
            HandleDrop(dropZone);

            Space(4);
            using (new EditorGUI.DisabledScope(_validateFile == null))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Validate JSON"))           DoValidate();
                if (GUILayout.Button("Extract Data to Fields"))  DoExtract();
            }

            if (!string.IsNullOrEmpty(_validateResult))
                EditorGUILayout.HelpBox(_validateResult, _validateSuccess ? UnityEditor.MessageType.Info : UnityEditor.MessageType.Error);
        }

        // ═════════════════════════════════════════════════════════════
        //  Status bar
        // ═════════════════════════════════════════════════════════════

        private void DrawStatus()
        {
            if (_isGenerating)
            {
                Rect r = GUILayoutUtility.GetRect(0, 22, GUILayout.ExpandWidth(true));
                EditorGUI.ProgressBar(r, _progress, $"Generating…  {Mathf.RoundToInt(_progress * 100)}%");
                Space(2);
                EditorGUILayout.LabelField(_statusMsg, EditorStyles.centeredGreyMiniLabel);
                Space(4);
            }

            if (!string.IsNullOrEmpty(_errorMsg))
            {
                var prev = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1f, 0.25f, 0.25f);
                EditorGUILayout.HelpBox("GENERATION FAILED\n\n" + _errorMsg, UnityEditor.MessageType.Error);
                GUI.backgroundColor = prev;
            }

            if (!string.IsNullOrEmpty(_successMsg))
            {
                var prev = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.25f, 0.95f, 0.4f);
                EditorGUILayout.HelpBox(_successMsg, UnityEditor.MessageType.Info);
                GUI.backgroundColor = prev;
            }
        }

        // ═════════════════════════════════════════════════════════════
        //  Generation
        // ═════════════════════════════════════════════════════════════

        private void TryGenerate()
        {
            _errorMsg   = "";
            _successMsg = "";

            var issues = GatherInputIssues();
            if (issues.Count > 0)
            {
                _errorMsg = "Fix these issues before generating:\n\n•  " + string.Join("\n•  ", issues);
                return;
            }

            _ = RunGenerationAsync();
        }

        private List<string> GatherInputIssues()
        {
            var issues = new List<string>();

            if (string.IsNullOrWhiteSpace(_scenario.idea))
                issues.Add("CONCEPT / IDEA is empty. Write a description of your scenario in the Scenario Content section.");

            if (_scenario.characters == null || _scenario.characters.Count == 0)
            {
                issues.Add("No characters defined. Add at least one character in the Scenario Content section.");
            }
            else
            {
                for (int i = 0; i < _scenario.characters.Count; i++)
                {
                    if (string.IsNullOrWhiteSpace(_scenario.characters[i].name))
                        issues.Add($"Character {i + 1}: Name is required.");
                    if (string.IsNullOrWhiteSpace(_scenario.characters[i].role))
                        issues.Add($"Character {i + 1}: Role is required.");
                }
            }

            if (string.IsNullOrWhiteSpace(_scenario.outputFolder))
                issues.Add("Output Folder is empty. Set it in the Output Settings section.");

            if (string.IsNullOrWhiteSpace(EffectivePrompt()))
                issues.Add("Generation Prompt is empty. Open the Generation Prompt section and click 'Reset to Default Prompt'.");

            if (_provider == null)
            {
                issues.Add("No LLM Provider asset assigned. Drag a ClaudeProvider, OpenAIChatProvider, or OllamaProvider into the Provider Asset slot in LLM Provider Settings.");
            }
            else
            {
                string model = ProviderModelName();
                if (string.IsNullOrWhiteSpace(model))
                    issues.Add("Provider's Model Name is empty. Open the provider asset and set the model ID.");

                if (!(_provider is OllamaProvider))
                {
                    string keyName = ProviderApiKeyName();
                    if (!ApiKeyStore.HasKey(keyName))
                        issues.Add($"API key '{keyName}' not found in {ApiKeyStore.KeyFilePath}. Open the key file and fill it in.");
                }
            }

            return issues;
        }

        private async Task RunGenerationAsync()
        {
            _isGenerating = true;
            _progress     = 0f;
            _genStartTime = EditorApplication.timeSinceStartup;
            _statusMsg    = "Building prompt…";
            _errorMsg     = "";
            _successMsg   = "";

            try
            {
                string sys  = EffectivePrompt();
                string user = BuildUserMessage();

                _statusMsg = $"Sending request to {_provider.ProviderDisplayName}…";

                string raw = await SendToProviderAsync(sys, user);

                _progress  = 0.88f;
                _statusMsg = "Parsing response…";

                if (string.IsNullOrWhiteSpace(raw))
                    throw new Exception("The AI returned an empty response.\nCheck your API key, model name, and that the provider is reachable.");

                string json = ExtractJson(raw);

                _progress  = 0.93f;
                _statusMsg = "Validating output…";

                var (config, errors) = ValidateJson(json);

                if (config == null)
                {
                    string preview = json.Length > 400 ? json.Substring(0, 400) + "…" : json;
                    throw new Exception(
                        "The AI produced invalid or incomplete JSON.\n\n" +
                        "Issues found:\n•  " + string.Join("\n•  ", errors) +
                        "\n\nFirst 400 chars of output:\n" + preview +
                        "\n\nTry:\n•  Increasing Max Output Tokens\n•  Lowering Temperature\n•  Using a more capable model");
                }

                if (errors.Count > 0)
                    Debug.LogWarning("[ScenarioGenerator] Minor issues in generated JSON (saved anyway):\n•  " + string.Join("\n•  ", errors));

                _progress  = 0.97f;
                _statusMsg = "Saving…";

                string saved = WriteJsonFile(json);
                AssetDatabase.Refresh();

                _progress     = 1f;
                _isGenerating = false;
                _statusMsg    = "";
                _successMsg   = $"Scenario saved successfully!\n\nPath: {saved}\n\nClick 'Open Output Folder' in Output Settings to find it.";
            }
            catch (Exception ex)
            {
                _isGenerating = false;
                _progress     = 0f;
                _statusMsg    = "";
                _errorMsg     = ex.Message;
                Debug.LogError("[ScenarioGenerator] " + ex);
            }
        }

        // ═════════════════════════════════════════════════════════════
        //  HTTP  (System.Net.Http — works in Editor without coroutines)
        // ═════════════════════════════════════════════════════════════

        private async Task<string> SendToProviderAsync(string sys, string user)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(_llm.timeoutSeconds) };

            if (_provider is ClaudeProvider)    return await SendClaudeAsync(client, sys, user);
            if (_provider is OllamaProvider)    return await SendOllamaAsync(client, sys, user);
            if (_provider is OpenAIChatProvider) return await SendOpenAIAsync(client, sys, user);

            throw new Exception($"Provider type '{_provider.GetType().Name}' is not supported by the Scenario Generator.");
        }

        private async Task<string> SendClaudeAsync(HttpClient c, string sys, string user)
        {
            c.DefaultRequestHeaders.Add("x-api-key",         ProviderApiKey());
            c.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

            string body =
                $"{{\"model\":\"{J(ProviderModelName())}\"," +
                $"\"max_tokens\":{_llm.maxTokens}," +
                $"\"temperature\":{F(_llm.temperature)}," +
                $"\"system\":\"{J(sys)}\"," +
                $"\"messages\":[{{\"role\":\"user\",\"content\":\"{J(user)}\"}}]}}";

            var resp = await c.PostAsync(
                "https://api.anthropic.com/v1/messages",
                new StringContent(body, Encoding.UTF8, "application/json"));
            string txt = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"Claude API returned HTTP {(int)resp.StatusCode}:\n{txt}");

            var parsed = JsonUtility.FromJson<AnthropicResp>(txt);
            if (parsed?.content?.Length > 0 && parsed.content[0]?.text != null)
                return parsed.content[0].text;
            throw new Exception("Could not read Claude response content.\nRaw:\n" + Trunc(txt));
        }

        private async Task<string> SendOpenAIAsync(HttpClient c, string sys, string user)
        {
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ProviderApiKey());

            string body =
                $"{{\"model\":\"{J(ProviderModelName())}\"," +
                $"\"messages\":[{{\"role\":\"system\",\"content\":\"{J(sys)}\"}},{{\"role\":\"user\",\"content\":\"{J(user)}\"}}]," +
                $"\"temperature\":{F(_llm.temperature)}," +
                $"\"max_tokens\":{_llm.maxTokens}," +
                $"\"top_p\":{F(_llm.topP)}}}";

            var resp = await c.PostAsync(
                ProviderOpenAIBaseUrl() + "/chat/completions",
                new StringContent(body, Encoding.UTF8, "application/json"));
            string txt = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"API returned HTTP {(int)resp.StatusCode}:\n{txt}");

            var parsed = JsonUtility.FromJson<OpenAIResp>(txt);
            if (parsed?.choices?.Length > 0 && parsed.choices[0]?.message?.content != null)
                return parsed.choices[0].message.content;
            throw new Exception("Could not read response content.\nRaw:\n" + Trunc(txt));
        }

        private async Task<string> SendOllamaAsync(HttpClient c, string sys, string user)
        {
            string body =
                $"{{\"model\":\"{J(ProviderModelName())}\"," +
                $"\"messages\":[{{\"role\":\"system\",\"content\":\"{J(sys)}\"}},{{\"role\":\"user\",\"content\":\"{J(user)}\"}}]," +
                $"\"stream\":false," +
                $"\"options\":{{\"temperature\":{F(_llm.temperature)},\"num_predict\":{_llm.maxTokens}," +
                $"\"top_p\":{F(_llm.topP)},\"top_k\":{(int)_llm.topK},\"repeat_penalty\":{F(_llm.repeatPenalty)}}}}}";

            var resp = await c.PostAsync(
                ProviderOllamaUrl() + "/api/chat",
                new StringContent(body, Encoding.UTF8, "application/json"));
            string txt = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"Ollama returned HTTP {(int)resp.StatusCode}:\n{txt}");

            var parsed = JsonUtility.FromJson<OllamaResp>(txt);
            if (parsed?.message?.content != null)
                return parsed.message.content;
            throw new Exception("Could not read Ollama response content.\nRaw:\n" + Trunc(txt));
        }

        // ═════════════════════════════════════════════════════════════
        //  Provider SO field readers (via SerializedObject)
        // ═════════════════════════════════════════════════════════════

        private static string SOString(ScriptableObject so, string field)
        {
            using var s = new SerializedObject(so);
            return s.FindProperty(field)?.stringValue ?? "";
        }

        private string ProviderApiKeyName()  => SOString(_provider, "apiKeyName");
        private string ProviderApiKey()      => ApiKeyStore.GetKey(ProviderApiKeyName());

        private string ProviderModelName()
        {
            if (_provider is OllamaProvider)
            {
                using var so = new SerializedObject(_provider);
                var models = so.FindProperty("availableModels");
                int idx    = so.FindProperty("selectedModelIndex")?.intValue ?? 0;
                if (models != null && idx >= 0 && idx < models.arraySize)
                {
                    string m = models.GetArrayElementAtIndex(idx)?.stringValue;
                    if (!string.IsNullOrEmpty(m)) return m;
                }
            }
            return _provider.ModelName;
        }

        private string ProviderOpenAIBaseUrl()
        {
            using var so = new SerializedObject(_provider);
            int preset = so.FindProperty("preset")?.enumValueIndex ?? 0;
            return preset switch
            {
                0 => "https://api.openai.com/v1",
                1 => "https://api.deepseek.com/v1",
                2 => "https://api.mistral.ai/v1",
                _ => (so.FindProperty("customBaseUrl")?.stringValue ?? "").TrimEnd('/')
            };
        }

        private string ProviderOllamaUrl()
        {
            string url = SOString(_provider, "baseUrl").TrimEnd('/');
            return string.IsNullOrEmpty(url) ? "http://localhost:11434" : url;
        }

        // ═════════════════════════════════════════════════════════════
        //  Prompt building
        // ═════════════════════════════════════════════════════════════

        private string EffectivePrompt() => _promptFile != null ? _promptFile.text : _basePrompt;

        private string BuildUserMessage()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Generate a complete scenario JSON for the following concept.");
            sb.AppendLine();
            sb.AppendLine($"CONCEPT: {_scenario.idea}");
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(_scenario.setting))             sb.AppendLine($"SETTING: {_scenario.setting}");
            if (!string.IsNullOrWhiteSpace(_scenario.timeframe))           sb.AppendLine($"TIMEFRAME: {_scenario.timeframe}");
            if (!string.IsNullOrWhiteSpace(_scenario.scenarioTitle))       sb.AppendLine($"TITLE HINT: {_scenario.scenarioTitle}");
            if (!string.IsNullOrWhiteSpace(_scenario.scenarioDescription)) sb.AppendLine($"DESCRIPTION HINT: {_scenario.scenarioDescription}");
            if (!string.IsNullOrWhiteSpace(_scenario.contextHint))         sb.AppendLine($"CONTEXT HINT (3-8 words): {_scenario.contextHint}");

            sb.AppendLine();
            sb.AppendLine($"CHARACTERS ({_scenario.characters.Count} total):");
            for (int i = 0; i < _scenario.characters.Count; i++)
            {
                var c = _scenario.characters[i];
                sb.AppendLine($"  [{i}] name={c.name}, role={c.role}");
                if (!string.IsNullOrWhiteSpace(c.personality))        sb.AppendLine($"       personality hint: {c.personality}");
                if (!string.IsNullOrWhiteSpace(c.background))         sb.AppendLine($"       background hint: {c.background}");
                if (!string.IsNullOrWhiteSpace(c.communicationStyle)) sb.AppendLine($"       communication_style hint: {c.communicationStyle}");
                if (!string.IsNullOrWhiteSpace(c.currentState))       sb.AppendLine($"       current_state hint: {c.currentState}");
                if (!string.IsNullOrWhiteSpace(c.teachingApproach))   sb.AppendLine($"       teaching_approach: {c.teachingApproach}");
            }

            sb.AppendLine();
            if (_scenario.includePlayer)
            {
                sb.AppendLine("PLAYER CHARACTER: include player_character block.");
                if (!string.IsNullOrWhiteSpace(_scenario.playerRole))         sb.AppendLine($"  role hint: {_scenario.playerRole}");
                if (!string.IsNullOrWhiteSpace(_scenario.playerDescription))  sb.AppendLine($"  description hint: {_scenario.playerDescription}");
                if (!string.IsNullOrWhiteSpace(_scenario.playerExpectations)) sb.AppendLine($"  expectations hint: {_scenario.playerExpectations}");
            }
            else sb.AppendLine("PLAYER CHARACTER: omit player_character entirely.");

            sb.AppendLine();
            if (_scenario.includeProgression)
            {
                if (_scenario.progressionSteps.Count > 0)
                {
                    sb.AppendLine($"PROGRESSION: include {_scenario.progressionSteps.Count} required_progression_steps in this order:");
                    for (int i = 0; i < _scenario.progressionSteps.Count; i++)
                    {
                        var s = _scenario.progressionSteps[i];
                        sb.AppendLine($"  step_id {i + 1}:");
                        if (!string.IsNullOrWhiteSpace(s.title))       sb.AppendLine($"    title hint: {s.title}");
                        if (!string.IsNullOrWhiteSpace(s.description)) sb.AppendLine($"    description hint: {s.description}");
                        if (s.keyPoints.Count > 0)                     sb.AppendLine($"    key_points hints: {string.Join(" | ", s.keyPoints)}");
                        if (s.teachingMoments.Count > 0)               sb.AppendLine($"    teaching_moments hints: {string.Join(" | ", s.teachingMoments)}");
                    }
                }
                else sb.AppendLine("PROGRESSION: include a sensible number of required_progression_steps (3–6) that fit the concept.");
            }
            else sb.AppendLine("PROGRESSION: omit required_progression_steps entirely.");

            sb.AppendLine();
            if (_scenario.includeSysInstructions)
            {
                sb.AppendLine("SYSTEM INSTRUCTIONS: include system_instructions block.");
                var si = _scenario.sysInstructions;
                if (!string.IsNullOrWhiteSpace(si.behaviorSectionLabel)) sb.AppendLine($"  behavior_section_label: {si.behaviorSectionLabel}");
                if (si.coreRules.Count > 0)             sb.AppendLine($"  core_rules MUST include: {string.Join(" | ", si.coreRules)}");
                if (si.interactionGuidelines.Count > 0) sb.AppendLine($"  interaction_guidelines MUST include: {string.Join(" | ", si.interactionGuidelines)}");
                if (si.criticalRules.Count > 0)         sb.AppendLine($"  critical_rules MUST include: {string.Join(" | ", si.criticalRules)}");
                if (si.behaviorGuidelines.Count > 0)    sb.AppendLine($"  behavior_guidelines MUST include: {string.Join(" | ", si.behaviorGuidelines)}");
            }
            else sb.AppendLine("SYSTEM INSTRUCTIONS: omit system_instructions.");

            sb.AppendLine();
            if (_scenario.includeConvInit)
            {
                sb.AppendLine("CONVERSATION INIT: include conversation_initialization block.");
                sb.AppendLine($"  first_speaker_index: {_scenario.firstSpeakerIndex}");
                if (!string.IsNullOrWhiteSpace(_scenario.openingPrompt))         sb.AppendLine($"  opening_prompt hint: {_scenario.openingPrompt}");
                if (!string.IsNullOrWhiteSpace(_scenario.conversationContext))   sb.AppendLine($"  context hint: {_scenario.conversationContext}");
                if (!string.IsNullOrWhiteSpace(_scenario.npcConversationPrompt)) sb.AppendLine($"  npc_conversation_prompt hint: {_scenario.npcConversationPrompt}");
                if (!string.IsNullOrWhiteSpace(_scenario.idlePlayerPrompt))      sb.AppendLine($"  idle_player_prompt hint: {_scenario.idlePlayerPrompt}");
            }
            else sb.AppendLine("CONVERSATION INIT: omit conversation_initialization.");

            if (!string.IsNullOrWhiteSpace(_scenario.additionalNotes))
            {
                sb.AppendLine();
                sb.AppendLine($"ADDITIONAL NOTES FROM DESIGNER: {_scenario.additionalNotes}");
            }

            sb.AppendLine();
            sb.AppendLine("Output the raw JSON object only. No markdown fences. No explanations.");
            return sb.ToString();
        }

        // ═════════════════════════════════════════════════════════════
        //  JSON utilities
        // ═════════════════════════════════════════════════════════════

        private static string ExtractJson(string raw)
        {
            string s = raw.Trim();
            if (s.StartsWith("```"))
            {
                int nl = s.IndexOf('\n');
                if (nl >= 0) s = s.Substring(nl + 1).Trim();
                int fence = s.LastIndexOf("```");
                if (fence >= 0) s = s.Substring(0, fence).Trim();
            }
            int first = s.IndexOf('{');
            int last  = s.LastIndexOf('}');
            return (first >= 0 && last > first) ? s.Substring(first, last - first + 1) : s;
        }

        private static (ScenarioConfig config, List<string> errors) ValidateJson(string json)
        {
            var errs = new List<string>();
            ScenarioConfig cfg;
            try { cfg = JsonUtility.FromJson<ScenarioConfig>(json); }
            catch (Exception ex) { errs.Add("JSON parse error: " + ex.Message); return (null, errs); }

            if (cfg == null)          { errs.Add("Parsed config is null."); return (null, errs); }
            if (cfg.scenario == null) { errs.Add("Missing 'scenario' object."); return (null, errs); }

            if (string.IsNullOrWhiteSpace(cfg.scenario.title))       errs.Add("scenario.title is empty");
            if (string.IsNullOrWhiteSpace(cfg.scenario.description)) errs.Add("scenario.description is empty");
            if (string.IsNullOrWhiteSpace(cfg.scenario.setting))     errs.Add("scenario.setting is empty");
            if (string.IsNullOrWhiteSpace(cfg.scenario.timeframe))   errs.Add("scenario.timeframe is empty");

            if (cfg.characters == null || cfg.characters.Length == 0)
                { errs.Add("No characters in output."); return (null, errs); }

            bool fatal = false;
            for (int i = 0; i < cfg.characters.Length; i++)
            {
                var c = cfg.characters[i];
                if (string.IsNullOrWhiteSpace(c.name))               { errs.Add($"characters[{i}].name empty");               fatal = true; }
                if (string.IsNullOrWhiteSpace(c.role))               { errs.Add($"characters[{i}].role empty");               fatal = true; }
                if (string.IsNullOrWhiteSpace(c.personality))         errs.Add($"characters[{i}].personality empty");
                if (string.IsNullOrWhiteSpace(c.background))          errs.Add($"characters[{i}].background empty");
                if (string.IsNullOrWhiteSpace(c.communication_style)) errs.Add($"characters[{i}].communication_style empty");
                if (string.IsNullOrWhiteSpace(c.current_state))       errs.Add($"characters[{i}].current_state empty");
            }

            return fatal ? (null, errs) : (cfg, errs);
        }

        private void DoValidate()
        {
            if (_validateFile == null) return;
            var (cfg, errs) = ValidateJson(_validateFile.text);
            if (cfg != null && errs.Count == 0)
            {
                _validateSuccess = true;
                _validateResult  =
                    $"VALID — ready to use.\n" +
                    $"  Characters: {cfg.characters.Length}\n" +
                    $"  Progression steps: {cfg.required_progression_steps?.Length ?? 0}\n" +
                    $"  Player character: {(cfg.player_character != null ? "yes" : "no")}\n" +
                    $"  System instructions: {(cfg.system_instructions != null ? "yes" : "no")}\n" +
                    $"  Conversation init: {(cfg.conversation_initialization != null ? "yes" : "no")}";
            }
            else if (cfg != null)
            {
                _validateSuccess = true;
                _validateResult  = "VALID with minor warnings:\n•  " + string.Join("\n•  ", errs);
            }
            else
            {
                _validateSuccess = false;
                _validateResult  = "INVALID — cannot be used by NPCManager:\n•  " + string.Join("\n•  ", errs);
            }
        }

        private void DoExtract()
        {
            if (_validateFile == null) return;
            ScenarioConfig cfg;
            try { cfg = JsonUtility.FromJson<ScenarioConfig>(_validateFile.text); }
            catch (Exception ex) { _validateResult = "Parse error: " + ex.Message; _validateSuccess = false; return; }
            if (cfg == null) { _validateResult = "Could not parse file."; _validateSuccess = false; return; }

            if (cfg.scenario != null)
            {
                _scenario.scenarioTitle       = cfg.scenario.title        ?? "";
                _scenario.scenarioDescription = cfg.scenario.description  ?? "";
                _scenario.setting             = cfg.scenario.setting      ?? "";
                _scenario.timeframe           = cfg.scenario.timeframe    ?? "";
                _scenario.contextHint         = cfg.scenario.context_hint ?? "";
            }

            if (cfg.characters != null && cfg.characters.Length > 0)
            {
                _scenario.characters.Clear();
                foreach (var c in cfg.characters)
                    _scenario.characters.Add(new CharacterInputData
                    {
                        name = c.name ?? "", role = c.role ?? "", personality = c.personality ?? "",
                        background = c.background ?? "", communicationStyle = c.communication_style ?? "",
                        currentState = c.current_state ?? "", teachingApproach = c.teaching_approach ?? "", foldout = true
                    });
            }

            _scenario.includePlayer = cfg.player_character != null;
            if (cfg.player_character != null)
            {
                _scenario.playerRole         = cfg.player_character.role         ?? "";
                _scenario.playerDescription  = cfg.player_character.description  ?? "";
                _scenario.playerExpectations = cfg.player_character.expectations ?? "";
            }

            _scenario.includeProgression = cfg.required_progression_steps?.Length > 0;
            _scenario.progressionSteps.Clear();
            if (cfg.required_progression_steps != null)
                foreach (var st in cfg.required_progression_steps)
                {
                    var s = new ProgressionStepInputData
                        { title = st.title ?? "", description = st.description ?? "", foldout = true };
                    if (st.key_points       != null) s.keyPoints.AddRange(st.key_points);
                    if (st.teaching_moments != null) s.teachingMoments.AddRange(st.teaching_moments);
                    _scenario.progressionSteps.Add(s);
                }

            _scenario.includeSysInstructions = cfg.system_instructions != null;
            if (cfg.system_instructions != null)
            {
                var si = _scenario.sysInstructions;
                si.behaviorSectionLabel = cfg.system_instructions.behavior_section_label ?? "";
                si.coreRules.Clear();            si.interactionGuidelines.Clear();
                si.criticalRules.Clear();        si.behaviorGuidelines.Clear();
                if (cfg.system_instructions.core_rules             != null) si.coreRules.AddRange(cfg.system_instructions.core_rules);
                if (cfg.system_instructions.interaction_guidelines != null) si.interactionGuidelines.AddRange(cfg.system_instructions.interaction_guidelines);
                if (cfg.system_instructions.critical_rules         != null) si.criticalRules.AddRange(cfg.system_instructions.critical_rules);
                if (cfg.system_instructions.behavior_guidelines    != null) si.behaviorGuidelines.AddRange(cfg.system_instructions.behavior_guidelines);
            }

            _scenario.includeConvInit = cfg.conversation_initialization != null;
            if (cfg.conversation_initialization != null)
            {
                _scenario.firstSpeakerIndex     = cfg.conversation_initialization.first_speaker_index;
                _scenario.openingPrompt         = cfg.conversation_initialization.opening_prompt          ?? "";
                _scenario.conversationContext   = cfg.conversation_initialization.context                 ?? "";
                _scenario.npcConversationPrompt = cfg.conversation_initialization.npc_conversation_prompt ?? "";
                _scenario.idlePlayerPrompt      = cfg.conversation_initialization.idle_player_prompt      ?? "";
            }

            _validateSuccess = true;
            _validateResult  = "Data extracted into fields. Review and click Generate to re-generate, or edit directly.";
        }

        private string WriteJsonFile(string json)
        {
            string folder = _scenario.outputFolder.TrimEnd('/').TrimEnd('\\');
            string abs    = ToAbsPath(folder);
            Directory.CreateDirectory(abs);

            string baseName = string.IsNullOrWhiteSpace(_scenario.filename)
                ? "scenario_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss")
                : string.Concat(_scenario.filename.Split(Path.GetInvalidFileNameChars()));

            string path = Path.Combine(abs, baseName + ".json");
            int v = 2;
            while (File.Exists(path)) { path = Path.Combine(abs, $"{baseName}_V{v}.json"); v++; }

            File.WriteAllText(path, json, new UTF8Encoding(false));
            return folder + "/" + Path.GetFileName(path);
        }

        // ═════════════════════════════════════════════════════════════
        //  Drag & drop
        // ═════════════════════════════════════════════════════════════

        private void HandleDrop(Rect zone)
        {
            var e = Event.current;
            if (!zone.Contains(e.mousePosition)) return;
            if (e.type == EventType.DragUpdated)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy; e.Use();
            }
            else if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                foreach (var o in DragAndDrop.objectReferences)
                    if (o is TextAsset ta) { _validateFile = ta; break; }
                e.Use();
            }
        }

        // ═════════════════════════════════════════════════════════════
        //  Persistence
        // ═════════════════════════════════════════════════════════════

        private void SaveSettings()
        {
            try
            {
                if (_provider != null)
                    EditorPrefs.SetString(K_PROVIDER, AssetDatabase.GetAssetPath(_provider));
                else
                    EditorPrefs.DeleteKey(K_PROVIDER);

                EditorPrefs.SetString(K_LLM,    JsonUtility.ToJson(_llm));
                EditorPrefs.SetString(K_SCENE,  JsonUtility.ToJson(_scenario));
                EditorPrefs.SetString(K_PROMPT, _basePrompt);
            }
            catch { }
        }

        private void LoadSettings()
        {
            try
            {
                if (EditorPrefs.HasKey(K_PROVIDER))
                {
                    string path = EditorPrefs.GetString(K_PROVIDER);
                    if (!string.IsNullOrEmpty(path))
                        _provider = AssetDatabase.LoadAssetAtPath<LLMProviderBase>(path);
                }

                if (EditorPrefs.HasKey(K_LLM))   JsonUtility.FromJsonOverwrite(EditorPrefs.GetString(K_LLM),   _llm);
                if (EditorPrefs.HasKey(K_SCENE))  JsonUtility.FromJsonOverwrite(EditorPrefs.GetString(K_SCENE), _scenario);
                _basePrompt = EditorPrefs.GetString(K_PROMPT, "");
                if (string.IsNullOrEmpty(_basePrompt)) _basePrompt = DefaultPrompt();

                _scenario.characters       ??= new List<CharacterInputData> { new CharacterInputData() };
                _scenario.progressionSteps ??= new List<ProgressionStepInputData>();
                _scenario.sysInstructions  ??= SysInstructionsInput.Defaults();
                _scenario.sysInstructions.coreRules             ??= new List<string>();
                _scenario.sysInstructions.interactionGuidelines ??= new List<string>();
                _scenario.sysInstructions.criticalRules         ??= new List<string>();
                _scenario.sysInstructions.behaviorGuidelines    ??= new List<string>();
            }
            catch { _basePrompt = DefaultPrompt(); }
        }

        // ═════════════════════════════════════════════════════════════
        //  Default system prompt
        // ═════════════════════════════════════════════════════════════

        private static string DefaultPrompt() =>
@"You are a scenario configuration generator for the LAS (Layered Animation System) NPC framework.

Your task is to generate a single, complete, valid JSON object conforming exactly to the ScenarioConfig schema.

CRITICAL OUTPUT RULES:
- Output ONLY the raw JSON object. Begin your response with { and end with }.
- Do NOT include markdown code fences (```), explanations, preamble, or any text outside the JSON.
- Every string value must be properly JSON-escaped (backslash, quote, newline, tab).
- Fill every required field fully. Do not use empty strings or null for required fields.
- Make all content coherent and internally consistent — names, roles, dialogue style, and steps should fit together naturally.

REQUIRED FIELDS (must be present and non-empty):
  scenario.title, scenario.description, scenario.setting, scenario.timeframe
  characters[i].name, characters[i].role, characters[i].personality,
  characters[i].background, characters[i].communication_style, characters[i].current_state

SCHEMA:
{
  ""scenario"": {
    ""title"": ""short scenario title"",
    ""description"": ""1-2 sentence overview shown to the player at scenario start"",
    ""setting"": ""physical location and environment"",
    ""timeframe"": ""when this takes place relative to a key event"",
    ""context_hint"": ""optional: 3-8 word phrase for internal AI alias generation""
  },
  ""characters"": [
    {
      ""name"": ""full name"",
      ""role"": ""job title / position"",
      ""personality"": ""personality traits and disposition, 1-2 sentences"",
      ""background"": ""professional history and expertise, 1-2 sentences"",
      ""communication_style"": ""how this character speaks, 1 sentence"",
      ""current_state"": ""what they are physically doing at scenario start, 1 sentence"",
      ""teaching_approach"": ""optional: specific teaching methodology""
    }
  ],
  ""player_character"": {
    ""role"": ""player's job title or role"",
    ""description"": ""1-2 sentence description of the player's character"",
    ""expectations"": ""what the player is expected to do""
  },
  ""required_progression_steps"": [
    {
      ""step_id"": 1,
      ""title"": ""phase name"",
      ""description"": ""1-2 sentences describing what happens"",
      ""key_points"": [""2-4 short facts or actions""],
      ""teaching_moments"": [""2-3 specific teaching opportunities""]
    }
  ],
  ""system_instructions"": {
    ""core_rules"": [""4-7 absolute rules the NPC must follow""],
    ""interaction_guidelines"": [""3-5 rules about NPC-player and NPC-NPC interaction""],
    ""critical_rules"": [""3-5 high-priority conversational rules""],
    ""behavior_guidelines"": [""4-6 domain-specific behavior guidelines""],
    ""behavior_section_label"": ""optional: section header, e.g. TEACHING BEHAVIOR""
  },
  ""conversation_initialization"": {
    ""first_speaker_index"": 0,
    ""opening_prompt"": ""instruction for the first NPC to start the conversation"",
    ""context"": ""brief description of the scene at scenario open"",
    ""npc_conversation_prompt"": ""optional: prompt for NPC auto-conversation continuation"",
    ""idle_player_prompt"": ""optional: prompt for when the player is idle""
  }
}

Generate the JSON now based on the concept and constraints in the user message.";

        // ═════════════════════════════════════════════════════════════
        //  UI helpers
        // ═════════════════════════════════════════════════════════════

        private void DrawSection(ref bool fold, string title, Action draw)
        {
            fold = EditorGUILayout.BeginFoldoutHeaderGroup(fold, title);
            if (fold) { EditorGUI.indentLevel++; draw(); EditorGUI.indentLevel--; }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void Field(string label, string desc, Action draw)
        {
            EditorGUILayout.LabelField(new GUIContent(label, desc), EditorStyles.boldLabel);
            EditorGUILayout.LabelField(desc, _descStyle);
            draw();
            Space(4);
        }

        private void ListField(string label, string desc, List<string> list)
        {
            EditorGUILayout.LabelField(new GUIContent(label, desc), EditorStyles.boldLabel);
            EditorGUILayout.LabelField(desc, _descStyle);
            for (int i = 0; i < list.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    list[i] = EditorGUILayout.TextField(list[i]);
                    if (GUILayout.Button("✕", GUILayout.Width(22))) { list.RemoveAt(i); break; }
                }
            }
            if (GUILayout.Button($"+ Add entry to {label}", EditorStyles.miniButton))
                list.Add("");
            Space(4);
        }

        private static void Bold(string text) => EditorGUILayout.LabelField(text, EditorStyles.boldLabel);
        private static void Note(string text) => EditorGUILayout.LabelField(text, EditorStyles.wordWrappedMiniLabel);
        private static void Space(int px)     => GUILayout.Space(px);
        private static bool Confirm(string t, string msg) => EditorUtility.DisplayDialog(t, msg, "Yes", "Cancel");

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
            _descStyle  = new GUIStyle(EditorStyles.wordWrappedMiniLabel) { wordWrap = true };
            _stylesReady = true;
        }

        // ═════════════════════════════════════════════════════════════
        //  String / path helpers
        // ═════════════════════════════════════════════════════════════

        private static string J(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }

        private static string F(float v)
            => v.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

        private static string Trunc(string s, int len = 500)
            => s.Length <= len ? s : s.Substring(0, len) + "…";

        private static string ToAbsPath(string assetPath)
        {
            string root = Application.dataPath.Replace("\\", "/").TrimEnd('/');
            root = root.Substring(0, root.Length - "/Assets".Length);
            return Path.Combine(root, assetPath.Replace("/", Path.DirectorySeparatorChar.ToString()));
        }
    }
}
#endif