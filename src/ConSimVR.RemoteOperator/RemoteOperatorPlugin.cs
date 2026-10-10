using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace ConSimVR.RemoteOperator
{
    internal enum OperatorMode
    {
        /// <summary>Secondary modes that reuse the driving cab camera and have an active hook (loader cranes), or have no cab camera.</summary>
        Auto,
        /// <summary>Any secondary mode that reuses the driving cab camera, hook or not.</summary>
        AnySecondaryMode,
        /// <summary>Never stand outside automatically (the toggle key still works).</summary>
        Off,
    }

    internal enum OperatorSide
    {
        Left,
        Right,
    }

    [BepInPlugin(Guid, "ConSimVR Remote Operator", "1.0.0")]
    [BepInDependency(ConSimVR.Plugin.Guid)]
    public class RemoteOperatorPlugin : BasePlugin
    {
        public const string Guid = "com.consimvr.remoteoperator";

        internal static ManualLogSource Logger;
        internal static ConfigEntry<OperatorMode> Mode;
        internal static ConfigEntry<OperatorSide> Side;
        internal static ConfigEntry<float> Distance;
        internal static ConfigEntry<float> EyeHeight;
        internal static ConfigEntry<float> OrbitStep;
        internal static ConfigEntry<KeyCode> ToggleKey;
        internal static ConfigEntry<KeyCode> ResetKey;
        internal static ConfigEntry<KeyCode> OrbitLeftKey;
        internal static ConfigEntry<KeyCode> OrbitRightKey;
        internal static ConfigEntry<KeyCode> CloserKey;
        internal static ConfigEntry<KeyCode> FartherKey;

        public override void Load()
        {
            Logger = Log;
            Mode = Config.Bind("General", "Mode", OperatorMode.Auto,
                "When to stand beside the machine. Auto: modes like the flatbed's loading crane (no operator seat of their own, a hook is active).");
            Side = Config.Bind("General", "Side", OperatorSide.Left, "Which side of the machine to stand on by default.");
            Distance = Config.Bind("General", "Distance", 7f, new ConfigDescription("Distance from the hook/crane, metres.", new AcceptableValueRange<float>(2f, 40f)));
            EyeHeight = Config.Bind("General", "EyeHeight", 1.7f, "Eye height above the ground for the standing operator, metres.");
            OrbitStep = Config.Bind("General", "OrbitStepDegrees", 30f, "How far the orbit keys move you around the machine.");
            ToggleKey = Config.Bind("Keys", "Toggle", KeyCode.F6, "Switch between standing beside the machine and the cab, for the current machine mode.");
            ResetKey = Config.Bind("Keys", "Reset", KeyCode.F7, "Go back to the default spot beside the machine.");
            OrbitLeftKey = Config.Bind("Keys", "OrbitLeft", KeyCode.Keypad4, "Walk around the machine anticlockwise.");
            OrbitRightKey = Config.Bind("Keys", "OrbitRight", KeyCode.Keypad6, "Walk around the machine clockwise.");
            CloserKey = Config.Bind("Keys", "Closer", KeyCode.Keypad8, "Step 1 m closer.");
            FartherKey = Config.Bind("Keys", "Farther", KeyCode.Keypad2, "Step 1 m back.");

            RemoteOperator.Instance = new RemoteOperator();
            VrApi.SetAnchorProvider(Guid, RemoteOperator.Instance.TryGetAnchor, priority: 10);
            AddComponent<RemoteOperatorBehaviour>();
            Log.LogInfo($"Remote operator view ready ({ToggleKey.Value} toggles cab/outside).");
        }
    }

    /// <summary>Keyboard controls for the operator position.</summary>
    public class RemoteOperatorBehaviour : MonoBehaviour
    {
        public RemoteOperatorBehaviour(IntPtr pointer) : base(pointer) { }

        private void Update()
        {
            var op = RemoteOperator.Instance;
            if (op == null || !VrApi.IsVrActive)
                return;
            if (Input.GetKeyDown(RemoteOperatorPlugin.ToggleKey.Value))
                op.Toggle();
            if (!op.IsActive)
                return;
            if (Input.GetKeyDown(RemoteOperatorPlugin.ResetKey.Value))
                op.ResetPlacement();
            if (Input.GetKeyDown(RemoteOperatorPlugin.OrbitLeftKey.Value))
                op.Orbit(-RemoteOperatorPlugin.OrbitStep.Value);
            if (Input.GetKeyDown(RemoteOperatorPlugin.OrbitRightKey.Value))
                op.Orbit(RemoteOperatorPlugin.OrbitStep.Value);
            if (Input.GetKeyDown(RemoteOperatorPlugin.CloserKey.Value))
                op.Step(-1f);
            if (Input.GetKeyDown(RemoteOperatorPlugin.FartherKey.Value))
                op.Step(1f);
        }
    }
}
