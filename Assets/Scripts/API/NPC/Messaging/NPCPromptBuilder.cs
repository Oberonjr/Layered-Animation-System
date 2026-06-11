using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace LAS
{
    /// <summary>
    /// Builds all LLM prompts for NPCManager.
    /// Constructed once per session with the scenario data and live-state references
    /// it needs; methods are pure transforms from that state to LLMRequest objects.
    /// </summary>
    public class NPCPromptBuilder
    {
        private readonly ScenarioConfig              _scenario;
        private readonly List<NPCController>         _npcs;
        private readonly List<ChatMessage>           _history;
        private readonly NPCActionDispatcher         _dispatcher;
        private readonly List<string>                _recentActionLog;
        private readonly ConversationPromptsSettings _conversationPrompts;
        private readonly RulesAndGuidelinesSettings  _rulesAndGuidelines;
        private readonly int                         _historyLimit;
        private readonly bool                        _includeProgression;

        public int CurrentProgressionStep { get; set; }

        public NPCPromptBuilder(
            ScenarioConfig              scenario,
            List<NPCController>         npcs,
            List<ChatMessage>           history,
            NPCActionDispatcher         dispatcher,
            List<string>                recentActionLog,
            ConversationPromptsSettings conversationPrompts,
            RulesAndGuidelinesSettings  rulesAndGuidelines,
            int                         historyLimit,
            bool                        includeProgression)
        {
            _scenario            = scenario;
            _npcs                = npcs;
            _history             = history;
            _dispatcher          = dispatcher;
            _recentActionLog     = recentActionLog;
            _conversationPrompts = conversationPrompts;
            _rulesAndGuidelines  = rulesAndGuidelines;
            _historyLimit        = historyLimit;
            _includeProgression  = includeProgression;
        }

        // ── Public API ────────────────────────────────────────────────────────────

        public LLMRequest BuildDialogueRequest(ConversationTurn turn) =>
            new LLMRequest
            {
                systemContent = BuildDialogueSystemContent(),
                history       = BuildConversationHistory(),
                userContent   = BuildDialogueUserContent(turn)
            };

        public LLMRequest BuildActionClassificationRequest(
            int              npcIndex,
            ConversationTurn turn,
            string           npcDialogue,
            string           npcActionIntent = null)
        {
            var sb = new StringBuilder();

            string npcName = IsValidIndex(npcIndex)
                ? _npcs[npcIndex].npcName : $"NPC {npcIndex}";

            string triggerLabel = turn.speakerType switch
            {
                SpeakerType.Player => "PLAYER",
                SpeakerType.NPC when IsValidIndex(turn.speakerIndex)
                    => _npcs[turn.speakerIndex].npcName,
                _ => "CONTEXT"
            };
            sb.AppendLine($"{triggerLabel}: \"{turn.content}\"");
            sb.AppendLine($"{npcName}: \"{npcDialogue}\"");

            if (!string.IsNullOrEmpty(npcActionIntent) &&
                !npcActionIntent.Equals("none", StringComparison.OrdinalIgnoreCase))
                sb.AppendLine($"{npcName} intends to: {npcActionIntent}");

            string sceneState = BuildSceneStateForActionPrompt();
            if (!string.IsNullOrEmpty(sceneState))
                sb.AppendLine(sceneState);

            if (_recentActionLog.Count > 0)
                sb.AppendLine($"Recent: {string.Join(" | ", _recentActionLog)}");
            sb.AppendLine();

            var dispatcher = _dispatcher ?? NPCActionDispatcher.Instance;
            if (dispatcher != null)
                sb.Append(dispatcher.BuildCompactActionVocabulary());

            sb.AppendLine("RULES:");
            sb.AppendLine("• Physical task (move/pick up/hand/place)? → use those keys. Do NOT add LOOK_AT_PLAYER.");
            sb.AppendLine("• No physical task? → always use LOOK_AT_PLAYER as default.");
            sb.AppendLine("• Chain steps in order. Use exact primary names from VALID TARGET NAMES.");
            sb.AppendLine();
            sb.AppendLine("Output ONLY JSON:");
            sb.AppendLine("{\"actions\":[{\"action_key\":\"KEY\",\"action_target\":\"name_or_empty\",\"action_secondary_target\":\"\"}]}");
            sb.AppendLine();
            sb.AppendLine("Examples:");
            sb.AppendLine("talking/no action → {\"actions\":[{\"action_key\":\"LOOK_AT_PLAYER\",\"action_target\":\"\",\"action_secondary_target\":\"\"}]}");
            sb.AppendLine("go to [location]  → {\"actions\":[{\"action_key\":\"GO_TO\",\"action_target\":\"Storage Area\",\"action_secondary_target\":\"\"}]}");
            sb.AppendLine("give [item]       → {\"actions\":[{\"action_key\":\"PICK_UP\",\"action_target\":\"Red Box\",\"action_secondary_target\":\"\"},{\"action_key\":\"HAND_TO_PLAYER\",\"action_target\":\"\",\"action_secondary_target\":\"\"}]}");

            return new LLMRequest
            {
                systemContent = "",
                history       = Array.Empty<LLMMessage>(),
                userContent   = sb.ToString()
            };
        }

        public string GetEffectiveNPCConversationPrompt()
        {
            if (!_conversationPrompts.overrideNPCConversationPrompt)
            {
                string fromJson = _scenario?.conversation_initialization?.npc_conversation_prompt;
                if (!string.IsNullOrWhiteSpace(fromJson)) return fromJson;
            }
            return _conversationPrompts.npcConversationPrompt;
        }

        public string GetEffectiveIdlePrompt()
        {
            if (!_conversationPrompts.overrideIdlePrompt)
            {
                string fromJson = _scenario?.conversation_initialization?.idle_player_prompt;
                if (!string.IsNullOrWhiteSpace(fromJson)) return fromJson;
            }
            return _conversationPrompts.idlePrompt;
        }

        // ── Dialogue system content ───────────────────────────────────────────────

        private string BuildDialogueSystemContent()
        {
            var sb = new StringBuilder();

            if (_scenario != null)
            {
                sb.AppendLine("=== SCENARIO CONTEXT ===");
                sb.AppendLine($"Setting: {_scenario.scenario.setting}");
                sb.AppendLine($"Timeframe: {_scenario.scenario.timeframe}");
                if (_scenario.conversation_initialization != null)
                    sb.AppendLine($"Context: {_scenario.conversation_initialization.context}");
                sb.AppendLine();

                if (_scenario.player_character != null)
                {
                    sb.AppendLine("=== PLAYER CHARACTER ===");
                    sb.AppendLine($"Role: {_scenario.player_character.role}");
                    sb.AppendLine($"Description: {_scenario.player_character.description}");
                    sb.AppendLine();
                }

                if (_scenario.system_instructions?.core_rules?.Length > 0)
                {
                    sb.AppendLine("=== CORE RULES (FOLLOW STRICTLY) ===");
                    foreach (var rule in _scenario.system_instructions.core_rules)
                        sb.AppendLine($"• {rule}");
                    sb.AppendLine();
                }

                sb.AppendLine("=== CHARACTERS (BY INDEX) ===");
                for (int i = 0; i < _scenario.characters.Length && i < _npcs.Count; i++)
                {
                    var ch = _scenario.characters[i];
                    sb.AppendLine($"NPC {i}: {ch.name} ({ch.role})");
                    sb.AppendLine($"  Personality: {ch.personality}");
                    sb.AppendLine($"  Background: {ch.background}");
                    sb.AppendLine($"  Communication: {ch.communication_style}");
                    if (!string.IsNullOrEmpty(ch.teaching_approach))
                        sb.AppendLine($"  Approach: {ch.teaching_approach}");
                    sb.AppendLine($"  Current: {ch.current_state}");
                }
                sb.AppendLine();

                var criticalRules = GetEffectiveCriticalRules();
                if (!string.IsNullOrWhiteSpace(criticalRules))
                {
                    sb.AppendLine("=== CRITICAL RULES ===");
                    sb.AppendLine(criticalRules.TrimEnd());
                    sb.AppendLine();
                }

                if (_scenario.system_instructions?.interaction_guidelines?.Length > 0)
                {
                    sb.AppendLine("=== INTERACTION GUIDELINES ===");
                    foreach (var g in _scenario.system_instructions.interaction_guidelines)
                        sb.AppendLine($"• {g}");
                    sb.AppendLine();
                }

                var behaviorGuidelines = GetEffectiveBehaviorGuidelines();
                if (!string.IsNullOrWhiteSpace(behaviorGuidelines))
                {
                    string sectionLabel = GetEffectiveBehaviorSectionLabel();
                    sb.AppendLine($"=== {sectionLabel} ===");
                    sb.AppendLine(behaviorGuidelines.TrimEnd());
                    sb.AppendLine();
                }
            }

            sb.AppendLine("=== RESPONSE FORMAT ===");
            sb.AppendLine("Output ONLY a JSON object — no descriptions, no prose, no text before or after the braces.");
            sb.AppendLine("Do NOT write 'description of', 'as she says', or any wrapper text. Just the JSON.");
            sb.AppendLine("You MUST use EXACTLY these three field names:");
            sb.AppendLine("  \"npc_index\"       : integer — who speaks");
            sb.AppendLine("  \"dialogue\"        : string  — the EXACT words spoken aloud");
            sb.AppendLine("  \"internal_thought\": string  — intended physical action, e.g. \"walking to the storage area\". Write \"none\" if no physical action.");
            sb.AppendLine("Do NOT add action_key or action_target — those are handled separately.");
            sb.AppendLine("Correct output:");
            sb.AppendLine("{\"npc_index\": 1, \"dialogue\": \"Let me grab that for you.\", \"internal_thought\": \"picking up the item and handing it to the player\"}");
            sb.AppendLine("{\"npc_index\": 0, \"dialogue\": \"Good question — the key thing to remember is...\", \"internal_thought\": \"none\"}");
            sb.AppendLine("WRONG (never do this):");
            sb.AppendLine("description of NPC response: {\"npc_index\": 1 ...");
            sb.Append("Output ONLY the JSON object starting with { and ending with }:");

            return sb.ToString();
        }

        private LLMMessage[] BuildConversationHistory()
        {
            var messages   = new List<LLMMessage>();
            int startIndex = Mathf.Max(0, _history.Count - _historyLimit);
            for (int i = startIndex; i < _history.Count; i++)
            {
                var msg = _history[i];
                if (msg.type == MessageType.Player)
                    messages.Add(new LLMMessage { role = "user", content = msg.message });
                else if (msg.type == MessageType.NPC)
                {
                    int npcIdx = _npcs.FindIndex(n => n.npcName == msg.speaker);
                    string histJson = $"{{\"npc_index\": {(npcIdx >= 0 ? npcIdx : 0)}, \"dialogue\": \"{EscapeJsonString(msg.message)}\", \"internal_thought\": \"none\"}}";
                    messages.Add(new LLMMessage { role = "assistant", content = histJson });
                }
            }
            return messages.ToArray();
        }

        private static string EscapeJsonString(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        private string BuildDialogueUserContent(ConversationTurn turn)
        {
            var sb = new StringBuilder();

            if (_scenario != null && _includeProgression && _scenario.required_progression_steps != null)
            {
                sb.AppendLine("=== REQUIRED PROGRESSION STEPS ===");
                for (int i = 0; i < _scenario.required_progression_steps.Length; i++)
                {
                    var step   = _scenario.required_progression_steps[i];
                    string status = i < CurrentProgressionStep  ? "[COMPLETED]" :
                                    i == CurrentProgressionStep ? "[CURRENT]"   : "[UPCOMING]";
                    sb.AppendLine($"{status} Step {step.step_id}: {step.title}");
                    if (i == CurrentProgressionStep)
                    {
                        sb.AppendLine($"  Description: {step.description}");
                        if (step.key_points?.Length > 0)
                            sb.AppendLine($"  Key points: {string.Join(", ", step.key_points)}");
                        if (step.teaching_moments?.Length > 0)
                            sb.AppendLine($"  Teaching moments: {string.Join(", ", step.teaching_moments)}");
                    }
                }
                sb.AppendLine();
            }

            sb.AppendLine(BuildCurrentInputSection(turn));
            AppendSpeakerSection(sb, turn);

            sb.AppendLine();
            sb.Append("Output ONLY valid JSON — no text outside the braces: {\"npc_index\": <int>, \"dialogue\": \"<spoken words>\", \"internal_thought\": \"<intended action or none>\"}");

            return sb.ToString().TrimEnd();
        }

        private string BuildCurrentInputSection(ConversationTurn turn)
        {
            var sb = new StringBuilder();
            switch (turn.speakerType)
            {
                case SpeakerType.Player:
                    sb.AppendLine(turn.content);
                    break;

                case SpeakerType.NPC:
                    string speakerName = IsValidIndex(turn.speakerIndex)
                        ? _npcs[turn.speakerIndex].npcName
                        : $"NPC {turn.speakerIndex}";
                    string targetName = IsValidIndex(turn.targetIndex)
                        ? _npcs[turn.targetIndex].npcName
                        : "";
                    sb.AppendLine($"{speakerName} just said: \"{turn.content}\"");
                    if (!string.IsNullOrEmpty(targetName))
                        sb.AppendLine($"You are {targetName}. Respond directly to {speakerName} by name — do not address the player in this turn.");
                    break;

                case SpeakerType.System:
                    sb.AppendLine($"[Stage direction — not spoken aloud]: {turn.content}");
                    break;

                case SpeakerType.Room:
                    sb.AppendLine($"[Scene situation]: {turn.content}");
                    if (IsValidIndex(turn.targetIndex))
                        sb.AppendLine($"You are {_npcs[turn.targetIndex].npcName}. React naturally to this situation.");
                    else
                        sb.AppendLine("One of you should react naturally to this situation.");
                    break;
            }
            return sb.ToString();
        }

        private void AppendSpeakerSection(StringBuilder prompt, ConversationTurn turn)
        {
            if (IsValidIndex(turn.targetIndex))
            {
                prompt.AppendLine($"=== YOU ARE NPC {turn.targetIndex} ===");
                prompt.AppendLine($"Respond as {_npcs[turn.targetIndex].npcName}");
            }
            else
            {
                prompt.AppendLine("=== DETERMINE WHO SHOULD RESPOND ===");
                prompt.AppendLine("Based on context and who was addressed, decide which NPC responds.");
            }
        }

        private bool IsValidIndex(int index) => index >= 0 && index < _npcs.Count;

        // ── Scene state ───────────────────────────────────────────────────────────

        private string BuildSceneStateForActionPrompt()
        {
            var sb       = new StringBuilder();
            var registry = NPCActionTargetRegistry.Instance;
            if (registry == null) return "";

            var items = registry.GetTargetsByType(TargetType.InteractableObject)
                .OfType<InteractableItem>()
                .ToList();
            if (items.Count > 0)
            {
                sb.Append("  Items: ");
                sb.AppendLine(string.Join(", ",
                    items.Select(i => $"{i.TargetName}({i.GetStateDescription()})")));
            }

            var npcLines = new List<string>();
            foreach (var npc in _npcs)
            {
                var b = npc.GetComponent<NPCBehaviourController>();
                if (b == null) continue;
                string held = b.IsHoldingObject
                    ? $"holding {b.HeldObject?.name ?? "object"}"
                    : "empty-handed";
                npcLines.Add($"{npc.npcName}({held})");
            }
            if (npcLines.Count > 0)
            {
                sb.Append("  NPCs:  ");
                sb.AppendLine(string.Join(", ", npcLines));
            }

            var locLines = new List<string>();
            foreach (var target in registry.GetTargetsByType(TargetType.Location).OfType<LocationTarget>())
            {
                string desc = target.GetStateDescription();
                if (desc != "empty") locLines.Add($"{target.TargetName}: {desc}");
            }
            if (locLines.Count > 0)
            {
                sb.AppendLine("  Locations:");
                foreach (var l in locLines) sb.AppendLine($"    {l}");
            }

            return sb.Length > 0 ? "SCENE STATE:\n" + sb : "";
        }

        // ── Effective-value helpers ───────────────────────────────────────────────

        private string GetEffectiveCriticalRules()
        {
            if (!_rulesAndGuidelines.overrideCriticalRules)
            {
                var fromJson = _scenario?.system_instructions?.critical_rules;
                if (fromJson != null && fromJson.Length > 0)
                    return string.Join("\n", Array.ConvertAll(fromJson, r => $"• {r}"));
            }
            return _rulesAndGuidelines.criticalRules;
        }

        private string GetEffectiveBehaviorGuidelines()
        {
            if (!_rulesAndGuidelines.overrideBehaviorGuidelines)
            {
                var fromJson = _scenario?.system_instructions?.behavior_guidelines;
                if (fromJson == null || fromJson.Length == 0)
                    fromJson = _scenario?.system_instructions?.teaching_behavior;
                if (fromJson != null && fromJson.Length > 0)
                    return string.Join("\n", Array.ConvertAll(fromJson, b => $"• {b}"));
            }
            return _rulesAndGuidelines.behaviorGuidelines;
        }

        private string GetEffectiveBehaviorSectionLabel()
        {
            if (!string.IsNullOrWhiteSpace(_rulesAndGuidelines.behaviorSectionLabelOverride))
                return _rulesAndGuidelines.behaviorSectionLabelOverride.ToUpper();
            string fromJson = _scenario?.system_instructions?.behavior_section_label;
            return string.IsNullOrWhiteSpace(fromJson) ? "BEHAVIOR GUIDELINES" : fromJson.ToUpper();
        }
    }
}
