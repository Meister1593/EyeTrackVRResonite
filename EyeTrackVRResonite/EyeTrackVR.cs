﻿using System;
using Elements.Core;
using FrooxEngine;
using HarmonyLib;
using ResoniteModLoader;

namespace EyeTrackVRResonite
{
    public class EyeTrackVR : ResoniteMod
    {
        public override string Name => "EyeTrackVRResonite";
        public override string Author => "PLYSHKA + dfgHiatus";
        public override string Version => "2.0.0";
        public override string Link => "https://github.com/Meister1593/EyeTrackVRResonite";

        public override void OnEngineInit()
        {
            _config = GetConfiguration();
            new Harmony("net.plyshka.EyeTrackVRResonite").PatchAll();
            Engine.Current.OnShutdown += ETVROSC.Teardown;
        }

        private static ETVROSC _etvr;
        private static ModConfiguration _config;

        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<bool> ModEnabled = new("enabled", "Mod Enabled", () => true);

        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<float> Alpha = new("alpha", "Eye Swing Multiplier X", () => 1.0f);

        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<float> Beta = new("beta", "Eye Swing Multiplier Y", () => 1.0f);

        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<int> OscPort = new("osc_port", "EyeTrackVR OSC port", () => 9000);

        [HarmonyPatch(typeof(InputInterface), MethodType.Constructor)]
        [HarmonyPatch(new[] { typeof(Engine) })]
        public class InputInterfaceCtorPatch
        {
            public static void Postfix(InputInterface __instance)
            {
                try
                {
                    _etvr = new ETVROSC(_config.GetValue(OscPort));
                    var gen = new EyeTrackVRInterface();
                    __instance.RegisterInputDriver(gen);
                }
                catch (Exception e)
                {
                    Warn("Module failed to initialize.");
                    Warn(e.ToString());
                }
            }
        }

        private class EyeTrackVRInterface : IInputDriver
        {
            private Eyes _eyes;
            private const float DefaultPupilSize = 0.0035f;
            public int UpdateOrder => 100;

            public void CollectDeviceInfos(DataTreeList list)
            {
                var eyeDataTreeDictionary = new DataTreeDictionary();
                eyeDataTreeDictionary.Add("Name", "EyeTrackVR Eye Tracking");
                eyeDataTreeDictionary.Add("Type", "Eye Tracking");
                eyeDataTreeDictionary.Add("Model", "ETVR Module");
                list.Add(eyeDataTreeDictionary);
            }

            public void RegisterInputs(InputInterface inputInterface)
            {
                _eyes = new Eyes(inputInterface, "EyeTrackVR Eye Tracking");
            }

            public void UpdateInputs(float deltaTime)
            {
                if (!_config.GetValue(ModEnabled))
                {
                    _eyes.IsEyeTrackingActive = false;
                    return;
                }

                _eyes.IsEyeTrackingActive = true;

                var fakeWiden = MathX.Remap(MathX.Clamp01(ETVROSC.EyeDataWithAddress["/avatar/parameters/EyesY"]), 0f,
                    1f, 0f, 0.33f);

                var leftEyeDirection = Project2DTo3D(ETVROSC.EyeDataWithAddress["/avatar/parameters/LeftEyeX"],
                    ETVROSC.EyeDataWithAddress["/avatar/parameters/EyesY"]);
                UpdateEye(leftEyeDirection, float3.Zero, true,
                    ETVROSC.EyeDataWithAddress["/avatar/parameters/EyesDilation"],
                    ETVROSC.EyeDataWithAddress["/avatar/parameters/LeftEyeLidExpandedSqueeze"],
                    fakeWiden, 0f, 0f, deltaTime, _eyes.LeftEye);

                var rightEyeDirection = Project2DTo3D(ETVROSC.EyeDataWithAddress["/avatar/parameters/RightEyeX"],
                    ETVROSC.EyeDataWithAddress["/avatar/parameters/EyesY"]);
                UpdateEye(rightEyeDirection, float3.Zero, true,
                    ETVROSC.EyeDataWithAddress["/avatar/parameters/EyesDilation"],
                    ETVROSC.EyeDataWithAddress["/avatar/parameters/RightEyeLidExpandedSqueeze"],
                    fakeWiden, 0f, 0f, deltaTime, _eyes.RightEye);

                var combinedDirection = MathX.Average(leftEyeDirection, rightEyeDirection);
                var combinedOpenness =
                    MathX.Average(ETVROSC.EyeDataWithAddress["/avatar/parameters/LeftEyeLidExpandedSqueeze"],
                        ETVROSC.EyeDataWithAddress["/avatar/parameters/RightEyeLidExpandedSqueeze"]);
                UpdateEye(combinedDirection, float3.Zero, true,
                    ETVROSC.EyeDataWithAddress["/avatar/parameters/EyesDilation"], combinedOpenness,
                    fakeWiden, 0f, 0f, deltaTime, _eyes.CombinedEye);
                _eyes.ComputeCombinedEyeParameters();

                _eyes.ConvergenceDistance = 0f;
                _eyes.Timestamp += deltaTime;
                _eyes.FinishUpdate();
            }

            private static void UpdateEye(float3 gazeDirection, float3 gazeOrigin, bool status, float pupilSize,
                float openness,
                float widen, float squeeze, float frown, float deltaTime, Eye eye)
            {
                eye.IsDeviceActive = Engine.Current.InputInterface.VR_Active;
                eye.IsTracking = status;

                if (eye.IsTracking)
                {
                    eye.UpdateWithDirection(gazeDirection);
                    eye.RawPosition = gazeOrigin;
                    eye.PupilDiameter = pupilSize != 0f ? pupilSize : DefaultPupilSize;
                }

                eye.Openness = openness;
                eye.Widen = widen;
                eye.Squeeze = squeeze;
                eye.Frown = frown;
            }

            private static float3 Project2DTo3D(float x, float y)
            {
                return new float3(MathX.Tan(_config.GetValue(Alpha) * x),
                    MathX.Tan(_config.GetValue(Beta) * y),
                    1f).Normalized;
            }
        }
    }
}