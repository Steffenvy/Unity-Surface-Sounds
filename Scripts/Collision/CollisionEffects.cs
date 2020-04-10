﻿/*
MIT License

Copyright (c) 2020 Steffen Vetne

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//Make the speeds and forces be individual to each surface type, so that you can slide on multiple? but that's unfair to both STs of different collisions being the same
//TODO: move the particles to always work?

namespace PrecisionSurfaceEffects
{
    public sealed partial class CollisionEffects : CollisionEffectsMaker, IMayNeedOnCollisionStay
    {
        //Constants
        public const bool CLAMP_FINAL_ONE_SHOT_VOLUME = true;
        private const float EXTRA_SEARCH_THICKNESS = 0.01f;
        private const int MAX_PARTICLE_TYPE_COUNT = 10;



        //Fields
#if UNITY_EDITOR
        [Header("Debug")]
        [Space(30)]
        [TextArea(1, 10)]
        //[ReadOnly]
        public string currentFrictionDebug;
#endif

        [Header("Quality")]
        [Space(30)]
        [Tooltip("Non-convex MeshCollider submeshes")]
        public bool findMeshColliderSubmesh = true;

        [Header("Impacts")]
        [Space(30)]
        public float impactCooldown = 0.1f;
        [Space(5)]
        public bool doImpactByImpulseChangeRate = true;
        public float impulseChangeRateToImpact = 10;

        [SeperatorLine]
        [Header("Sounds")]
        [Space(30)]
        public SurfaceSoundSet soundSet;

        [Header("Scaling")]
        [Space(5)]
        [Tooltip("To easily make the speed be local/relative")]
        public float speedMultiplier = 1;
        public float forceMultiplier = 1;
        public float totalVolumeMultiplier = 0.3f;
        public float totalPitchMultiplier = 1;

        [Header("Impact Sound")]
        [Space(5)]
        public Sound impactSound = new Sound();

        [Header("Friction Sound")]
        [Space(5)]
        public bool doFrictionSound = true;
        public FrictionSound frictionSound = new FrictionSound(); //[Tooltip("When the friction soundType changes, this can be used to play impactSound")]

        [Header("Vibration Sound")]
        [Space(5)]
        public bool doVibrationSound = true;
        public VibrationSound vibrationSound = new VibrationSound();

        [SeperatorLine]
        [Header("Particles")]
        [Space(30)]
        public ParticlesType particlesType = ParticlesType.ImpactAndFriction;
        public SurfaceParticleSet particleSet;
        public Particles particles = new Particles();

        private float impactCooldownT;
        private SurfaceOutputs outputs2 = new SurfaceOutputs();
        private SurfaceOutputs outputs3 = new SurfaceOutputs();
        private readonly SurfaceOutputs averageOutputs = new SurfaceOutputs(); // List<CollisionSound> collisionSounds = new List<CollisionSound>();
        private readonly List<int> givenSources = new List<int>();
        private float weightedSpeed;
        private float forceSum;
        private bool downShifted;
        private float previousImpulse;

        [SerializeField]
        [HideInInspector]
        private OnCollisionStayer stayer;

        public OnSurfaceCallback onEnterParticles; //should I remove these?
        public OnSurfaceCallback onEnterSound;



        //Properties
#if UNITY_EDITOR
        public bool NeedOnCollisionStay
        {
            get
            {
                return particlesType == ParticlesType.ImpactAndFriction || doFrictionSound || doImpactByImpulseChangeRate;
            }
        }
#endif



        //Methods
        private void GetSurfaceTypeOutputs(Collision c, bool doSound, bool doParticle, out SurfaceOutputs soundOutputs, out SurfaceOutputs particleOutputs)
        {
            soundOutputs = particleOutputs = null;

            if (doSound || doParticle)
            {
                SurfaceOutputs GetFlipFlopOutputs()
                {
                    var temp = SurfaceData.outputs;
                    SurfaceData.outputs = outputs2;
                    outputs2 = temp;
                    return temp;
                }

                if (findMeshColliderSubmesh && c.collider is MeshCollider mc && !mc.convex)
                {
                    var contact = c.GetContact(0);
                    var pos = contact.point;
                    var norm = contact.normal; //this better be normalized!

                    float searchThickness = EXTRA_SEARCH_THICKNESS + Mathf.Abs(contact.separation);

                    if (mc.Raycast(new Ray(pos + norm * searchThickness, -norm), out RaycastHit rh, Mathf.Infinity)) //searchThickness * 2
                    {
#if UNITY_EDITOR
                        float debugSize = 3;
                        Debug.DrawLine(pos + norm * debugSize, pos - norm * debugSize, Color.white, 0);
#endif

                        SurfaceOutputs GetOutputs(SurfaceData data)
                        {
                            SurfaceData.outputs.Clear();
                            data.AddSurfaceTypes(c.collider, pos, triangleIndex: rh.triangleIndex);
                            return GetFlipFlopOutputs();
                        }

                        //Sound Outputs
                        if (doSound)
                            soundOutputs = GetOutputs(soundSet.data);
                        if (doParticle)
                            particleOutputs = GetOutputs(particleSet.data);

                        return;
                    }
                }

                if (doSound)
                {
                    soundSet.data.GetCollisionSurfaceTypes(c);
                    soundOutputs = GetFlipFlopOutputs();
                }

                if (doParticle)
                {
                    particleSet.data.GetCollisionSurfaceTypes(c);
                    particleOutputs = GetFlipFlopOutputs();
                }
            }
        }

        private Vector3 CurrentRelativeVelocity(ContactPoint contact)
        {
            //return collision.relativeVelocity.magnitude;

            Vector3 Vel(Rigidbody rb, Vector3 pos)
            {
                if (rb == null)
                    return Vector3.zero;
                return rb.GetPointVelocity(pos);
            }

            //This version takes into account angular, I believe Unity's doesn't

            //TODO: make it use multiple contacts?

            var vel = Vel(contact.thisCollider.attachedRigidbody, contact.point);
            var ovel = Vel(contact.otherCollider.attachedRigidbody, contact.point);

            return (vel - ovel); //.magnitude;
        }

        private bool Stop(Collision collision)
        {
            Transform target;
            if (collision.rigidbody != null)
                target = collision.rigidbody.transform;
            else
                target = collision.collider.transform;

            var otherCSM = target.GetComponent<CollisionEffectsMaker>();
            if (otherCSM != null)
            {
                if (otherCSM.priority == priority)
                    return (otherCSM.gameObject.GetInstanceID() > gameObject.GetInstanceID());

                return priority < otherCSM.priority;
            }
            return false;
        }

        private void DoParticles(Collision c, SurfaceOutputs outputs, float dt)
        {
            if (particleSet == null || outputs.Count == 0)
                return;

            SurfaceParticles.GetData(c, out float impulse, out float speed, out Quaternion rot, out Vector3 center, out float radius, out Vector3 vel0, out Vector3 vel1, out float mass0, out float mass1);

            for (int i = 0; i < outputs.Count; i++)
            {
                var o = outputs[i];

                var sp = particleSet.GetSurfaceParticles(ref o);
                
                if (sp != null)
                {
                    sp.GetInstance().PlayParticles
                    (
                        o.color, o.particleCountMultiplier * particles.particleCountMultiplier, o.particleSizeMultiplier * particles.particleSizeMultiplier,
                        o.weight,
                        impulse, speed,
                        rot, center, radius + particles.minimumParticleShapeRadius, outputs.hitNormal,
                        vel0, vel1,
                        mass0, mass1,
                        dt
                    );
                }
            }
        }
        private void DoFrictionSound(Collision collision, SurfaceOutputs outputs, float impMag, Vector3 normImp)
        {
            float speed = 0;
            float impulse = 0;
            int contactCount = collision.contactCount;
            for (int i = 0; i < contactCount; i++)
            {
                var contact = collision.GetContact(0);
                var norm = contact.normal;
                impulse += impMag * Mathf.Lerp(1, frictionSound.frictionNormalForceMultiplier, Mathf.Abs(Vector3.Dot(normImp, norm))); //I'm not sure if this works

                speed += CurrentRelativeVelocity(contact).magnitude;
            }
            float invCount = 1 / contactCount;
            impulse *= invCount;
            speed *= speedMultiplier * invCount; // Vector3.ProjectOnPlane(CurrentRelativeVelocity(collision), contact.normal).magnitude; // collision.relativeVelocity.magnitude;

            var force = impulse / Time.deltaTime; //force = Mathf.Max(0, Mathf.Min(frictionSound.maxForce, force) - frictionSound.minForce);
            force *= frictionSound.SpeedFader(speed); //So that it is found the maximum with this in mind

            if (force > 0)
            {
                forceSum += force;
                weightedSpeed += force * speed;

                float influence = force / forceSum;
                float invInfluence = 1 - influence;

                for (int i = 0; i < outputs.Count; i++)
                {
                    var output = outputs[i];

                    bool success = false;
                    for (int ii = 0; ii < averageOutputs.Count; ii++)
                    {
                        var sumOutput = averageOutputs[ii];
                        if (sumOutput.surfaceTypeID == output.surfaceTypeID && sumOutput.particleOverrides == output.particleOverrides)
                        {
                            void Lerp(ref float from, float to)
                            {
                                from = invInfluence * from + influence * to;
                            }

                            Lerp(ref sumOutput.weight, output.weight);
                            Lerp(ref sumOutput.volumeMultiplier, output.volumeMultiplier);
                            Lerp(ref sumOutput.pitchMultiplier, output.pitchMultiplier);
                            Lerp(ref sumOutput.particleSizeMultiplier, output.particleSizeMultiplier);
                            Lerp(ref sumOutput.particleCountMultiplier, output.particleCountMultiplier);
                            sumOutput.color = invInfluence * sumOutput.color + influence * output.color;

                            success = true;
                            break;
                        }
                    }

                    if (!success)
                    {
                        averageOutputs.Add
                        (
                            new SurfaceOutput()
                            {
                                weight = output.weight * influence,

                                surfaceTypeID = output.surfaceTypeID,
                                volumeMultiplier = output.volumeMultiplier,
                                pitchMultiplier = output.pitchMultiplier,
                                particleSizeMultiplier = output.particleSizeMultiplier,
                                particleCountMultiplier = output.particleCountMultiplier,
                                color = output.color,
                            }
                        );
                    }
                }
            }
        }

        internal void OnOnCollisionStay(Collision collision)
        {
            if (!isActiveAndEnabled)
                return;
            if (Stop(collision))
                return;

            var doParticles = particlesType == ParticlesType.ImpactAndFriction;
            GetSurfaceTypeOutputs(collision, doFrictionSound, doParticles, out SurfaceOutputs soundOutputs, out SurfaceOutputs particleOutputs);

            var imp = collision.impulse;
            var impMag = forceMultiplier * imp.magnitude;
            var normImp = imp.normalized;

            //Impact By Impulse ChangeRate
            if (doImpactByImpulseChangeRate)
            {
                if ((impMag - previousImpulse) / Time.deltaTime >= impulseChangeRateToImpact)
                    OnCollisionEnter(collision);
                previousImpulse = impMag;
            }

            //Friction Sounds
            if (doFrictionSound)
                DoFrictionSound(collision, soundOutputs, impMag, normImp);

            //Particles
            if (doParticles)
            {
                particleOutputs.Downshift(MAX_PARTICLE_TYPE_COUNT, particles.minimumTypeWeight);
                DoParticles(collision, particleOutputs, Time.deltaTime);
            }
        }



        //Datatypes
        public enum ParticlesType { None, ImpactOnly, ImpactAndFriction }

        public delegate void OnSurfaceCallback(Collision collision, SurfaceOutputs outputs);



        //Lifecycle
        private void Awake()
        {
            frictionSound.sources = new FrictionSound.Source[frictionSound.audioSources.Length];
            for (int i = 0; i < frictionSound.sources.Length; i++)
            {
                frictionSound.sources[i] = new FrictionSound.Source() { audioSource = frictionSound.audioSources[i] };
            }
        }

        private void OnEnable()
        {
            if (stayer != null)
                stayer.onOnCollisionStay += OnOnCollisionStay;
        }
        private void OnDisable()
        {
            if (stayer != null)
                stayer.onOnCollisionStay -= OnOnCollisionStay;

            for (int i = 0; i < frictionSound.audioSources.Length; i++)
                frictionSound.audioSources[i].Pause();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            stayer = GetComponent<OnCollisionStayer>();
            if (stayer == null && NeedOnCollisionStay)
                stayer = gameObject.AddComponent<OnCollisionStayer>();

            if (!Application.isPlaying)
                currentFrictionDebug = "(This only works in playmode)";

            impactSound.Validate(false);
            frictionSound.Validate(true);
        }
#endif

        private void FixedUpdate()
        {
            averageOutputs.Clear(); // collisionSounds.Clear();
            downShifted = false;
            weightedSpeed = 0;
            forceSum = 0;
        }

        internal void OnCollisionEnter(Collision collision)
        {
            if (!isActiveAndEnabled)
                return;


            //Impact Sound
            if (impactCooldownT <= 0)
            {
                //This prevents multiple sounds for one collision
                if (Stop(collision))
                    return;

                var speed = speedMultiplier * collision.relativeVelocity.magnitude; //Can't consistently use CurrentRelativeVelocity(collision);, probably maybe because it's too late to get that speed (already resolved)
                var force = forceMultiplier * collision.impulse.magnitude;//Here "force" is actually an impulse
                var vol = totalVolumeMultiplier * impactSound.Volume(force) * impactSound.SpeedFader(speed);

                if (vol > 0.00000000000001f)
                {
                    impactCooldownT = impactCooldown;

                    bool doParticles = particlesType != ParticlesType.None;
                    GetSurfaceTypeOutputs(collision, true, doParticles, out SurfaceOutputs soundOutputs, out SurfaceOutputs particleOutputs); //, maxc);

                    //Impact Sound
                    var pitch = totalPitchMultiplier * impactSound.Pitch(speed);

                    int maxc = impactSound.audioSources.Length;
                    soundOutputs.Downshift(maxc, impactSound.minimumTypeWeight);

                    var c = Mathf.Min(maxc, soundOutputs.Count);
                    for (int i = 0; i < c; i++)
                    {
                        var output = soundOutputs[i];
                        var st = soundSet.surfaceTypeSounds[output.surfaceTypeID];
                        var voll = vol * output.weight * output.volumeMultiplier;
                        if (CLAMP_FINAL_ONE_SHOT_VOLUME)
                            voll = Mathf.Min(voll, 1);
                        st.PlayOneShot(impactSound.audioSources[i], voll, pitch * output.pitchMultiplier);
                    }

                    if (onEnterSound != null)
                        onEnterSound(collision, soundOutputs);


                    //Impact Particles
                    if (doParticles)
                    {
                        float approximateCollisionDuration = 1 / Mathf.Max(0.00000001f, particles.selfHardness * soundOutputs.hardness);

                        particleOutputs.Downshift(MAX_PARTICLE_TYPE_COUNT, particles.minimumTypeWeight);
                        DoParticles(collision, particleOutputs, approximateCollisionDuration);

                        if (onEnterParticles != null)
                            onEnterParticles(collision, particleOutputs);
                    }
                }
            }
        }

        private void Update()
        {
            impactCooldownT -= Time.deltaTime;

            if (doFrictionSound)
            {
                //Downshifts and reroutes
                if (!downShifted)
                {
                    downShifted = true;

                    //Re-sorts them
                    averageOutputs.SortDescending();

                    //Downshifts
                    var maxCount = frictionSound.sources.Length;
                    averageOutputs.Downshift(maxCount, frictionSound.minimumTypeWeight);

                    //Clears Givens
                    for (int i = 0; i < maxCount; i++)
                        frictionSound.sources[i].given = false;
                    givenSources.Clear();

                    //Sees if any of them are aligned
                    for (int outputID = 0; outputID < averageOutputs.Count; outputID++)
                    {
                        var c = Mathf.Min(maxCount, averageOutputs.Count); //?

                        var output = averageOutputs[outputID];
                        var clip = soundSet.surfaceTypeSounds[output.surfaceTypeID].frictionSound;

                        //Finds and assigns sources that match the clip already
                        int givenSource = -1;
                        for (int sourceID = 0; sourceID < frictionSound.sources.Length; sourceID++)
                        {
                            var source = frictionSound.sources[sourceID];

                            if (source.clip == clip || source.Silent || source.clip == null || source.clip.clip == null)
                            {
                                source.ChangeClip(clip, this);

                                givenSource = sourceID;
                                source.given = true;
                                break;
                            }
                        }
                        givenSources.Add(givenSource);
                    }

                    //Changes Clips
                    for (int outputID = 0; outputID < averageOutputs.Count; outputID++)
                    {
                        var output = averageOutputs[outputID];

                        //If it wasn't given a source
                        if (givenSources[outputID] == -1)
                        {
                            var clip = soundSet.surfaceTypeSounds[output.surfaceTypeID].frictionSound;

                            for (int sourceID = 0; sourceID < frictionSound.sources.Length; sourceID++)
                            {
                                var source = frictionSound.sources[sourceID];

                                if (!source.given)
                                {
                                    source.given = true;

                                    source.ChangeClip(clip, this);
                                    givenSources[outputID] = sourceID;

                                    break;
                                }
                            }
                        }
                    }
                }


#if UNITY_EDITOR
                currentFrictionDebug = "";
#endif

                float speed = 0;
                if (forceSum > 0) //prevents a divide by zero
                    speed = weightedSpeed / forceSum;

                //Updates the sources which have been given
                for (int outputID = 0; outputID < averageOutputs.Count; outputID++)
                {
                    var output = averageOutputs[outputID];
                    var source = frictionSound.sources[givenSources[outputID]];

#if UNITY_EDITOR
                    var st = soundSet.surfaceTypeSounds[output.surfaceTypeID];
                    currentFrictionDebug = currentFrictionDebug + st.name + " V: " + output.weight + " P: " + output.pitchMultiplier + "\n";
#endif

                    var vm = totalVolumeMultiplier * output.volumeMultiplier;
                    var pm = totalPitchMultiplier * output.pitchMultiplier;
                    source.Update(frictionSound, vm, pm, forceSum * output.weight, speed);
                }

                //Updates the sources which haven't been given
                for (int i = 0; i < frictionSound.sources.Length; i++)
                {
                    var source = frictionSound.sources[i];
                    if (!source.given)
                    {
                        source.Update(frictionSound, totalVolumeMultiplier, totalPitchMultiplier, 0, 0);
                    }
                }
            }
        }
    }
}

/*
 *         //var norm = collision.GetContact(0).normal;

        //Debug.DrawRay(collision.GetContact(0).point, collision.impulse.normalized * 3);

        //Friction Sound
        //var force = Vector3.ProjectOnPlane(collision.impulse, norm).magnitude / Time.deltaTime; //Finds tangent force
        //var impulse = collision.impulse;
        //var force = (1 - Vector3.Dot(impulse.normalized, norm)) * impulse.magnitude / Time.deltaTime; //Finds tangent force
            //Debug.Log(collision.collider.gameObject.name + " " + collision.impulse.magnitude);
*/
