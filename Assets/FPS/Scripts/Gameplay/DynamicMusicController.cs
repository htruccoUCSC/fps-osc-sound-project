using UnityEngine;
using Unity.FPS.Game;
using Unity.FPS.AI;

namespace Unity.FPS.Gameplay
{
    public class DynamicMusicController : MonoBehaviour
    {
        [Header("Distance Settings")]
        [Tooltip("Distance at which the music is at max intensity (fastest tempo, loudest threat layers)")]
        public float MinDetectionDistance = 5.0f;

        [Tooltip("Distance at which the music is at base intensity (slowest tempo, threat layers silent)")]
        public float MaxDetectionDistance = 25.0f;

        [Tooltip("How fast the intensity updates (lerp speed) to prevent sudden jumps")]
        public float SmoothingSpeed = 2.0f;

        [Tooltip("Time interval between OSC messages in seconds (to avoid network flood)")]
        public float UpdateInterval = 0.1f;

        [Header("Tempo Settings (milliseconds)")]
        [Tooltip("Metronome speed (interval in ms) when at base intensity (far from enemies)")]
        public float TempoMin = 400f;

        [Tooltip("Metronome speed (interval in ms) when at max intensity (close to enemies)")]
        public float TempoMax = 100f;

        [Header("Volume Layers (0 to 1)")]
        [Range(0f, 1f)] public float BassVolMin = 0.8f;
        [Range(0f, 1f)] public float BassVolMax = 1.0f;

        [Range(0f, 1f)] public float MelodyVolMin = 0.5f;
        [Range(0f, 1f)] public float MelodyVolMax = 0.8f;

        [Range(0f, 1f)] public float DrumVolMin = 0.0f;
        [Range(0f, 1f)] public float DrumVolMax = 0.8f;

        [Range(0f, 1f)] public float SnareVolMin = 0.0f;
        [Range(0f, 1f)] public float SnareVolMax = 0.8f;

        private ActorsManager m_ActorsManager;
        private EnemyManager m_EnemyManager;
        private float m_CurrentIntensity = 0.0f;
        private float m_LastUpdateTime = 0.0f;

        // Track last sent values to avoid sending redundant OSC messages
        private float m_LastSentTempo = -1f;
        private float m_LastSentBassVol = -1f;
        private float m_LastSentMelodyVol = -1f;
        private float m_LastSentDrumVol = -1f;
        private float m_LastSentSnareVol = -1f;

        void Start()
        {
            m_ActorsManager = FindAnyObjectByType<ActorsManager>();
            m_EnemyManager = FindAnyObjectByType<EnemyManager>();

            if (m_ActorsManager == null)
            {
                Debug.LogError("ActorsManager not found in the scene! DynamicMusicController will not function.");
            }
            if (m_EnemyManager == null)
            {
                Debug.LogError("EnemyManager not found in the scene! DynamicMusicController will not function.");
            }
        }

        void Update()
        {
            if (m_ActorsManager == null || m_EnemyManager == null) return;

            GameObject player = m_ActorsManager.Player;
            if (player == null)
            {
                // Fallback: search for player tag if ActorsManager has not set it yet
                player = GameObject.FindWithTag("Player");
                if (player == null)
                {
                    // Smoothly decay intensity to 0 if player is dead or missing
                    UpdateIntensity(0.0f);
                    return;
                }
            }

            // Find closest enemy distance
            float minDistance = Mathf.Infinity;
            bool anyActiveEnemies = false;

            if (m_EnemyManager.Enemies != null)
            {
                for (int i = 0; i < m_EnemyManager.Enemies.Count; i++)
                {
                    EnemyController enemy = m_EnemyManager.Enemies[i];
                    if (enemy != null)
                    {
                        anyActiveEnemies = true;
                        float distance = Vector3.Distance(player.transform.position, enemy.transform.position);
                        if (distance < minDistance)
                        {
                            minDistance = distance;
                        }
                    }
                }
            }

            float targetIntensity = 0.0f;
            if (anyActiveEnemies && minDistance < MaxDetectionDistance)
            {
                // Calculate normalized intensity: 0 (at MaxDetectionDistance) to 1 (at MinDetectionDistance or closer)
                targetIntensity = 1.0f - Mathf.Clamp01((minDistance - MinDetectionDistance) / (MaxDetectionDistance - MinDetectionDistance));
            }

            // Smooth the intensity transition
            m_CurrentIntensity = Mathf.MoveTowards(m_CurrentIntensity, targetIntensity, SmoothingSpeed * Time.deltaTime);

            // Throttle OSC sends to prevent congestion
            if (Time.time - m_LastUpdateTime >= UpdateInterval)
            {
                m_LastUpdateTime = Time.time;
                SendOSCUpdates();
            }
        }

        private void SendOSCUpdates()
        {
            if (OSCHandler.Instance == null) return;

            // Map smoothed intensity to values
            float targetTempo = Mathf.Lerp(TempoMin, TempoMax, m_CurrentIntensity);
            float targetBass = Mathf.Lerp(BassVolMin, BassVolMax, m_CurrentIntensity);
            float targetMelody = Mathf.Lerp(MelodyVolMin, MelodyVolMax, m_CurrentIntensity);
            float targetDrum = Mathf.Lerp(DrumVolMin, DrumVolMax, m_CurrentIntensity);
            float targetSnare = Mathf.Lerp(SnareVolMin, SnareVolMax, m_CurrentIntensity);

            // Send OSC messages only if they changed significantly
            const float epsilon = 0.01f;

            if (Mathf.Abs(m_LastSentTempo - targetTempo) > epsilon)
            {
                OSCHandler.Instance.SendMessageToClient("pd", "/unity/tempo", targetTempo);
                m_LastSentTempo = targetTempo;
            }

            if (Mathf.Abs(m_LastSentBassVol - targetBass) > epsilon)
            {
                OSCHandler.Instance.SendMessageToClient("pd", "/unity/bass_vol", targetBass);
                m_LastSentBassVol = targetBass;
            }

            if (Mathf.Abs(m_LastSentMelodyVol - targetMelody) > epsilon)
            {
                OSCHandler.Instance.SendMessageToClient("pd", "/unity/melody_vol", targetMelody);
                m_LastSentMelodyVol = targetMelody;
            }

            if (Mathf.Abs(m_LastSentDrumVol - targetDrum) > epsilon)
            {
                OSCHandler.Instance.SendMessageToClient("pd", "/unity/drum_vol", targetDrum);
                m_LastSentDrumVol = targetDrum;
            }

            if (Mathf.Abs(m_LastSentSnareVol - targetSnare) > epsilon)
            {
                OSCHandler.Instance.SendMessageToClient("pd", "/unity/snare_vol", targetSnare);
                m_LastSentSnareVol = targetSnare;
            }
        }

        private void UpdateIntensity(float targetVal)
        {
            m_CurrentIntensity = Mathf.MoveTowards(m_CurrentIntensity, targetVal, SmoothingSpeed * Time.deltaTime);
            if (Time.time - m_LastUpdateTime >= UpdateInterval)
            {
                m_LastUpdateTime = Time.time;
                SendOSCUpdates();
            }
        }
    }
}
