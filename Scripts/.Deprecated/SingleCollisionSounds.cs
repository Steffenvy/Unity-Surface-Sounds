﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SurfaceSounds;

public class SingleCollisionSounds : MonoBehaviour
{
    //Fields
#if UNITY_EDITOR
    [Header("Debug")]
    public string currentSurfaceTypeDebug;
#endif

    [Header("Quality")]
    [Space(30)]
    [Tooltip("Non-convex MeshCollider submeshes")]
    public bool findMeshColliderSubmesh = true;

    [Header("Scaling")]
    [Tooltip("To easily make the speed be local/relative")]
    public float speedMultiplier = 1;
    public float forceMultiplier = 1;
    public float totalVolumeMultiplier = 0.3f;
    public float totalPitchMultiplier = 1;

    [Header("Sounds")]
    public SurfaceSoundSet soundSet;

    [Header("Friction Sound")]
    [Space(15)]
    public FrictionSound frictionSound = new FrictionSound();
    [Tooltip("When the friction soundType changes, this can be used to play impactSound")]

    [Header("Impact Sound")]
    [Space(15)]
    public Sound impactSound = new Sound();
    public float impactCooldown = 0.1f;
    public FrictionTypeChangeImpact frictionTypeChangeImpact = new FrictionTypeChangeImpact();

    private float impactCooldownT;

    private readonly List<CollisionSound> collisionSounds = new List<CollisionSound>();



    //Methods
    private int GetSurfaceTypeID(Collision c)
    {
        if (findMeshColliderSubmesh && c.collider is MeshCollider mc && !mc.convex)
        {
            var contact = c.GetContact(0);
            var pos = contact.point;
            var norm = contact.normal; //this better be normalized!

            float searchThickness = 0.001f + Mathf.Abs(contact.separation);

            if (mc.Raycast(new Ray(pos + norm * searchThickness, -norm), out RaycastHit rh, Mathf.Infinity)) //searchThickness * 2
            {
#if UNITY_EDITOR
                float debugSize = 3;
                Debug.DrawLine(pos + norm * debugSize, pos - norm * debugSize, Color.white, 0);
#endif

                return soundSet.types.GetSurfaceType(c.collider, pos, rh.triangleIndex);
            }
        }

        return soundSet.types.GetCollisionSurfaceTypeID(c);
    }



    //Datatypes
    private struct CollisionSound
    {
        public float force, speed;
        public SurfaceSoundSet.SurfaceTypeSounds s;
    }

    [System.Serializable]
    public class FrictionTypeChangeImpact //Just for foldering
    {
        [Range(0, 1)]
        public float probability = 1;
        public float volumeMultiplier = 1;
        public float pitchMultiplier = 1;
    }

    [System.Serializable]
    public class Sound
    {
        //Fields
        public AudioSource audioSource;

        [Header("Volume")]
        public float volumeByForce = 0.1f; //public float baseVolume = 0;
        public Vector2 volumeFaderBySpeedRange = new Vector2(0.01f, 0.1f);

        [Header("Pitch")]
        public float basePitch = 0.5f;
        public float pitchBySpeed = 0.035f;


        //Methods
        public float SpeedFader(float speed)
        {
            return Mathf.Clamp01(Mathf.InverseLerp(volumeFaderBySpeedRange.x, volumeFaderBySpeedRange.y, speed));
        }

        public float Volume(float force)
        {
            return volumeByForce * force; //baseVolume + 
        }

        public float Pitch(float speed)
        {
            return basePitch + pitchBySpeed * speed;
        }

#if UNITY_EDITOR
        public void Validate()
        {
            volumeFaderBySpeedRange.x = Mathf.Max(0, volumeFaderBySpeedRange.x);
            volumeFaderBySpeedRange.y = Mathf.Max(volumeFaderBySpeedRange.x, volumeFaderBySpeedRange.y);
        }
#endif
    }

    [System.Serializable]
    public class FrictionSound : Sound
    {
        //Fields
        [Header("Rates")]
        public float clipChangeSmoothTime = 0.001f;
        [Tooltip("This is used in smoothing the volume and pitch")]
        public SmoothTimes smoothTimes = SmoothTimes.Default(); //make it be smoothtime instead?

        [Header("Force")]
        public float minForce = 1;
        public float maxForce = 100;

        internal SurfaceSoundSet.SurfaceTypeSounds.Clip clip;
        private float currentVolume;
        private float currentPitch;
        private float volumeVelocity;
        private float pitchVelocity;


        //Datatypes
        [System.Serializable]
        public struct SmoothTimes
        {
            public float up;
            public float down;

            public static SmoothTimes Default()
            {
                return new SmoothTimes()
                {
                    up = 0.05f,
                    down = .15f
                };
            }
        }


        //Methods
        public void ChangeClip(SurfaceSoundSet.SurfaceTypeSounds sts, CollisionSounds cs)
        {
            if (clip != sts.loopSound)
            {
                if (cs.impactCooldownT <= 0)
                {
                    cs.impactCooldownT = cs.impactCooldown;

                    var ftci = cs.frictionTypeChangeImpact;

                    if (Audible(currentVolume) && Random.value < ftci.probability)
                        sts.PlayOneShot(cs.impactSound.audioSource, ftci.volumeMultiplier * currentVolume, ftci.pitchMultiplier * currentPitch);
                }

                clip = sts.loopSound;
            }
        }
        public void Update(float totalVolumeMultiplier, float totalPitchMultiplier, float force, float speed)
        {
            if (clip == null)
                return;

            float targetPitch = clip.pitchMultiplier * Pitch(speed);

            if (audioSource.clip != clip.clip)
            {
                //Changes the clip if silent
                if (!Audible(currentVolume))
                {
                    audioSource.clip = clip.clip;
                    currentPitch = targetPitch; //Immediately changes the pitch
                    volumeVelocity = pitchVelocity = 0;
                }

                //Fades the volume to change the clip
                SmoothDamp(ref currentVolume, 0, ref volumeVelocity, new SmoothTimes() { down = clipChangeSmoothTime });
                audioSource.volume = currentVolume;
            }
            else
            {
                if (audioSource.clip == null)
                {
                    if (audioSource.isPlaying)
                        audioSource.Stop();
                }
                else
                {
                    //Smoothly fades the pitch and volume
                    float lerpedAmount = SmoothDamp(ref currentVolume, clip.volumeMultiplier * Volume(force), ref volumeVelocity, smoothTimes);
                    audioSource.volume = totalVolumeMultiplier * currentVolume;

                    if (speed != 0)
                        SmoothDamp(ref currentPitch, targetPitch, ref pitchVelocity, smoothTimes); // Mathf.LerpUnclamped(currentPitch, targetPitch, lerpedAmount);
                    audioSource.pitch = totalPitchMultiplier * currentPitch;


                    //Ensures the AudioSource is only playing if the volume is high enough
                    bool audible = Audible(currentVolume);
                    if (audible && !audioSource.isPlaying)
                        audioSource.Play();
                    if (!audible && audioSource.isPlaying)
                        audioSource.Pause(); //perhaps Stop()?
                }
            }
        }
        private static float SmoothDamp(ref float value, float target, ref float velocity, SmoothTimes rates)
        {
            float smoothTime;
            if (target > value)
                smoothTime = rates.up;
            else
                smoothTime = rates.down;

            float maxChange = Time.deltaTime * smoothTime;

            var wantedChange = target - value;
            //var clampedChange = Mathf.Clamp(wantedChange, -maxChange, maxChange);
            //value += clampedChange;

            var before = value;
            value = Mathf.SmoothDamp(value, target, ref velocity, smoothTime);
            float clampedChange = value - before;

            if (wantedChange == 0)
                return 1;
            else
                return clampedChange / wantedChange; //returns the amount it has lerped, basically what the t would be in a Mathf.Lerp(value, target, t);
        }
        private static bool Audible(float vol)
        {
            return vol > 0.00000001f;
        }
    }



    //Lifecycle
#if UNITY_EDITOR
    private void Prepare(AudioSource source, bool loop)
    {
        source.loop = loop;
        source.playOnAwake = false;
    }
    private void OnValidate()
    {
        currentSurfaceTypeDebug = "";

        Prepare(impactSound.audioSource, false);
        Prepare(frictionSound.audioSource, true);
    }
#endif

    private void FixedUpdate()
    {
        collisionSounds.Clear();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (impactCooldownT <= 0)
        {
            impactCooldownT = impactCooldown;

            //Impact Sound
            var speed = speedMultiplier * collision.relativeVelocity.magnitude;
            var force = forceMultiplier * collision.impulse.magnitude;//Here "force" is actually an impulse
            var vol = totalVolumeMultiplier * impactSound.Volume(force) * impactSound.SpeedFader(speed);
            var pitch = totalPitchMultiplier * impactSound.Pitch(speed);

            var st = soundSet.sounds[GetSurfaceTypeID(collision)];
#if UNITY_EDITOR
            currentSurfaceTypeDebug = st.autoGroupName;
#endif
            st.PlayOneShot(impactSound.audioSource, vol, pitch);
        }
    }
    private void OnCollisionStay(Collision collision)
    {
        var force = forceMultiplier * collision.impulse.magnitude / Time.deltaTime;
        force = Mathf.Max(0, Mathf.Min(frictionSound.maxForce, force) - frictionSound.minForce);
        var speed = speedMultiplier * collision.relativeVelocity.magnitude;
        force *= frictionSound.SpeedFader(speed); //So that it is found the maximum with this in mind

        var s = soundSet.sounds[GetSurfaceTypeID(collision)];
#if UNITY_EDITOR
        currentSurfaceTypeDebug = s.autoGroupName;
#endif

        bool succeeded = false;
        for (int i = 0; i < collisionSounds.Count; i++)
        {
            var cs = collisionSounds[i];

            if (cs.s == s)
            {
                cs.force += force;
                cs.speed += force * speed; //weights speed, so that it can find a weighted average pitch for all the potential OnCollisionStays
                collisionSounds[i] = cs;

                succeeded = true;
                break;
            }
        }

        if (!succeeded)
        {
            collisionSounds.Add
            (
                new CollisionSound()
                {
                    s = s,
                    force = force,
                    speed = force * speed
                }
            );
        }
    }

    private void Update()
    {
        impactCooldownT -= Time.deltaTime;


        //Finds the maximum sound
        float maxForce = 0;
        CollisionSound max = new CollisionSound();
        for (int i = 0; i < collisionSounds.Count; i++)
        {
            var cs = collisionSounds[i];
            if (cs.force > maxForce)
            {
                maxForce = cs.force;
                max = cs;
            }
        }
        if (max.s != null)
            frictionSound.ChangeClip(max.s, this);

        float speed = 0;
        if (max.force > 0) //prevents a divide by zero
            speed = max.speed / max.force;

        frictionSound.Update(totalVolumeMultiplier, totalPitchMultiplier, max.force, speed);
    }
}