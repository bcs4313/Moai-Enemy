using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Unity.Netcode;

namespace MoaiEnemy.src.MoaiNormal
{
    class EngineAudioController : NetworkBehaviour
    {

        public MoaiEnemyAI moai;

        public AudioSource runningSound;
        public float runningMinVolume = 0.3f;
        public float runningMaxVolume = 1f;
        public float runningMaxPitch = 3f;
        public float runningMinPitch = 0.3f;

        public AudioSource idleSound;
        public float idleMinVolume = 0.1f;
        public float idleMaxVolume = 1f;
        public float idleMaxPitch = 3f;


        void Start()
        {

        }

        void Update()
        {
            if (moai.vehicleController.currentlyRiding)
            {
                if (!runningSound.isPlaying) { runningSound.Play(); }
                if (!idleSound.isPlaying) { idleSound.Play(); }
                moai.speedRatio = getSpeedRatio();
                idleSound.volume = Mathf.Lerp(idleMinVolume, idleMaxVolume, moai.speedRatio) * Plugin.moaiGlobalMusicVol.Value;
                runningSound.volume = Mathf.Lerp(runningMinVolume, runningMaxVolume, moai.speedRatio) * Plugin.moaiGlobalMusicVol.Value;
                runningSound.pitch = Mathf.Lerp(runningSound.pitch, Mathf.Lerp(runningMinPitch, runningMaxPitch, moai.speedRatio), Time.deltaTime);
            }
            else
            {
                if (runningSound.isPlaying) { runningSound.Stop(); }
                if (idleSound.isPlaying) { idleSound.Stop(); }
            }
        }

        public float getSpeedRatio()
        {
            var controller = moai.vehicleController;
            var gas = Math.Clamp((controller.currentlyRiding.moveInputVector * 5).y, 0.5, 1);  // forward input from player
            return (float)((moai.speedClamped * gas) / moai.vehicleMaxSpeed);
        }
    }
}
