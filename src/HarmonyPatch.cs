using System;
using System.Collections.Generic;
using Vintagestory.Common;
using Vintagestory.Client.NoObf;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Client;
using ProtoBuf;

using HarmonyLib;

using Cairo;

namespace FreeAim
{
    [HarmonyPatch(typeof(SystemRenderPlayerAimAcc))]
    class SystemRenderPlayerAimAccPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch("OnRenderFrame2DOverlay")]
        static bool OnRenderFrame2DOverlayPrefix()
        {
            return false;
        }
    }

    [HarmonyPatch(typeof(SystemRenderAim))]
    class SystemRenderAimPatch
    {
        private static int aimRangedTextureId;

        private static int aimRangedTextureYellowId;
        private static int aimRangedTextureRedId;

        [HarmonyPrefix]
        [HarmonyPatch("OnBlockTexturesLoaded")]
        static bool OnBlockTexturesLoadedPrefix(ClientMain ___game)
        {
            aimRangedTextureId = (___game.Api as ICoreClientAPI).Render.GetOrLoadTexture(new AssetLocation("freeaim", "gui/targetranged.png"));

            // TESTING STUFF, DELETE LATER
            aimRangedTextureYellowId = (___game.Api as ICoreClientAPI).Render.GetOrLoadTexture(new AssetLocation("freeaim", "gui/targetranged_yellow.png"));
            aimRangedTextureRedId = (___game.Api as ICoreClientAPI).Render.GetOrLoadTexture(new AssetLocation("freeaim", "gui/targetranged_red.png"));
            
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch("DrawAim")]
        static bool DrawAimPrefix(int ___aimTextureId, ClientMain ___game)
        {
            if (FreeAimCore.aiming)
            {
                //___game.Render2DTexture(aimRangedTextureYellowId, ___game.Width / 2 - 16 + FreeAimCore.aimX, ___game.Height / 2 - 16 + FreeAimCore.aimY, 32, 32, 10000f);
                ___game.Render2DTexture(aimRangedTextureId, ___game.Width / 2 - 16 + FreeAimCore.aimX + FreeAimCore.aimOffsetX, ___game.Height / 2 - 16 + FreeAimCore.aimY + FreeAimCore.aimOffsetY, 32, 32, 10000f);

                return false;
            }
            
            return true;
        }
    }

    [HarmonyPatch(typeof(ClientMain))]
    class ClientMainPatch
    {
        static float horizontalLimit = 0.125f;
        static float verticalLimit = 0.35f;
        static float verticalOffset = -0.15f;

        static float driftFrequency = 0.001f;
        static float driftMagnitude = 150f;
        static float driftMax = 150f;
        public static float driftMultiplier = 1f;

        static long twitchDuration = 300;
        static float twitchMagnitude = 40f;
        static float twitchMax = 5f;
        public static float twitchMultiplier = 1f;

        // ---

        private static NormalizedSimplexNoise noisegen = NormalizedSimplexNoise.FromDefaultOctaves(4, 1.0, 0.9, 123L);

        static long twitchLastChangeMilliseconds;
        static long twitchLastStepMilliseconds;

        public static float twitchX;
        public static float twitchY;
        public static float twitchLength;

        static Random random;

        [HarmonyPrefix]
        [HarmonyPatch("UpdateCameraYawPitch")]
        static bool UpdateCameraYawPitchPrefix(ClientMain __instance, 
            ref double ___MouseDeltaX, ref double ___MouseDeltaY, 
            ref double ___DelayedMouseDeltaX, ref double ___DelayedMouseDeltaY,
            ClientPlatformAbstract ___Platform,
            float dt)
        {
            if (FreeAimCore.aiming)
            {
                // = Aiming system #3 - simpler, Receiver-inspired =
                // Aim drift
                //FreeAimCore.aimOffsetX += ((float)noisegen.Noise(__instance.ElapsedMilliseconds * driftFrequency, 1000f) - 0.5f) * driftMagnitude * dt;
                //FreeAimCore.aimOffsetY += ((float)noisegen.Noise(-1000f, __instance.ElapsedMilliseconds * driftFrequency) - 0.5f) * driftMagnitude * dt;

                float horizontalRatio = __instance.Width / 1920f;
                float verticalRatio = __instance.Height / 1200f;

                FreeAimCore.aimOffsetX += (((float)noisegen.Noise(__instance.ElapsedMilliseconds * driftFrequency, 1000f) - 0.5f) - FreeAimCore.aimOffsetX / (driftMax * driftMultiplier)) * driftMagnitude * driftMultiplier * dt * horizontalRatio;
                FreeAimCore.aimOffsetY += (((float)noisegen.Noise(-1000f, __instance.ElapsedMilliseconds * driftFrequency) - 0.5f) - FreeAimCore.aimOffsetY / (driftMax * driftMultiplier)) * driftMagnitude * driftMultiplier * dt * verticalRatio;

                if (__instance.Api.World.ElapsedMilliseconds > twitchLastChangeMilliseconds + twitchDuration)
                {
                    twitchLastChangeMilliseconds = __instance.Api.World.ElapsedMilliseconds;
                    twitchLastStepMilliseconds = __instance.Api.World.ElapsedMilliseconds;

                    if (random == null)
                    {
                        random = new Random((int)(__instance.EntityPlayer.EntityId + __instance.Api.World.ElapsedMilliseconds));
                    }

                    twitchX = (((float)random.NextDouble() - 0.5f) * 2f) * (twitchMax * twitchMultiplier) - FreeAimCore.aimOffsetX / (twitchMax * twitchMultiplier);
                    twitchY = (((float)random.NextDouble() - 0.5f) * 2f) * (twitchMax * twitchMultiplier) - FreeAimCore.aimOffsetY / (twitchMax * twitchMultiplier);

                    twitchLength = GameMath.Sqrt(twitchX * twitchX + twitchY * twitchY);

                    twitchX = twitchX / twitchLength;
                    twitchY = twitchY / twitchLength;
                }

                float lastStep = (twitchLastStepMilliseconds - twitchLastChangeMilliseconds) / (float)twitchDuration;
                float currentStep = (__instance.Api.World.ElapsedMilliseconds - twitchLastChangeMilliseconds) / (float)twitchDuration;

                float stepSize = ((1f - lastStep) * (1f - lastStep)) - ((1f - currentStep) * (1f - currentStep));

                //FreeAimCore.aimOffsetX += twitchX * stepSize * twitchMagnitude * dt;
                //FreeAimCore.aimOffsetY += twitchY * stepSize * twitchMagnitude * dt;

                FreeAimCore.aimOffsetX += twitchX * stepSize * (twitchMagnitude * twitchMultiplier * dt) * (twitchDuration / 20) * horizontalRatio;
                FreeAimCore.aimOffsetY += twitchY * stepSize * (twitchMagnitude * twitchMultiplier * dt) * (twitchDuration / 20) * verticalRatio;

                twitchLastStepMilliseconds = __instance.Api.World.ElapsedMilliseconds;

                // Aiming itself
                float horizontalAimLimit = __instance.Width / 2f * horizontalLimit;
                float verticalAimLimit = __instance.Height / 2f * verticalLimit;
                float verticalAimOffset = __instance.Height / 2f * verticalOffset;

                float deltaX = (float)(___MouseDeltaX - ___DelayedMouseDeltaX);
                float deltaY = (float)(___MouseDeltaY - ___DelayedMouseDeltaY);

                if (Math.Abs(FreeAimCore.aimX + deltaX) > horizontalAimLimit)
                {
                    FreeAimCore.aimX = FreeAimCore.aimX > 0 ? horizontalAimLimit : -horizontalAimLimit;
                }
                else
                {
                    FreeAimCore.aimX += deltaX;
                    ___DelayedMouseDeltaX = ___MouseDeltaX;
                }

                if (Math.Abs(FreeAimCore.aimY + deltaY - verticalAimOffset) > verticalAimLimit)
                {
                    FreeAimCore.aimY = (FreeAimCore.aimY > 0 ? verticalAimLimit : -verticalAimLimit) + verticalAimOffset;
                }
                else
                {
                    FreeAimCore.aimY += deltaY;
                    ___DelayedMouseDeltaY = ___MouseDeltaY;
                }

                FreeAimCore.clientInstance.SetAim();
            }
            
            return true;
        }
    }
}