using UnityEngine;
using System.Collections;

[System.Serializable]
[UnityEngine.RequireComponent(typeof(PerFrameRaycast))]
public partial class AutoFire : MonoBehaviour
{
    public GameObject bulletPrefab;
    public Transform spawnPoint;
    public float frequency;
    public float coneAngle;
    public bool firing;
    public float damagePerSecond;
    public float forcePerSecond;
    public float hitSoundVolume;
    public GameObject muzzleFlashFront;
    [Tooltip("每次开火点亮枪口火焰的时长（秒）。")]
    public float muzzleFlashDuration = 0.05f;
    private float lastFireTime;
    private float muzzleFlashHideTime = -1f;
    private AudioSource fireAudio;
    private PerFrameRaycast raycast;
    public virtual void Awake()
    {
        this.HideMuzzleFlash();
        this.raycast = this.GetComponent<PerFrameRaycast>();
        this.fireAudio = this.GetComponent<AudioSource>();
        if (this.spawnPoint == null)
        {
            this.spawnPoint = this.transform;
        }
    }

    public virtual void Update()
    {
        if (this.muzzleFlashHideTime > 0f && Time.time >= this.muzzleFlashHideTime)
        {
            this.HideMuzzleFlash();
        }

        if (this.firing)
        {
            float interval = this.frequency > 0.01f ? 1f / this.frequency : 0.1f;
            if (Time.time > (this.lastFireTime + interval))
            {
                // Spawn visual bullet
                Quaternion coneRandomRotation = Quaternion.Euler(Random.Range(-this.coneAngle, this.coneAngle), Random.Range(-this.coneAngle, this.coneAngle), 0);
                GameObject go = Spawner.Spawn(this.bulletPrefab, this.spawnPoint.position, this.spawnPoint.rotation * coneRandomRotation) as GameObject;
                SimpleBullet bullet = go.GetComponent<SimpleBullet>();
                this.lastFireTime = Time.time;
                this.ShowMuzzleFlash();
                // Find the object hit by the raycast
                RaycastHit hitInfo = this.raycast.GetHitInfo();
                if (hitInfo.transform)
                {
                    // Get the health component of the target if any
                    Health targetHealth = hitInfo.transform.GetComponent<Health>();
                    if (targetHealth)
                    {
                        // Apply damage
                        targetHealth.OnDamage(this.damagePerSecond / this.frequency, -this.spawnPoint.forward);
                    }
                    // Get the rigidbody if any
                    if (hitInfo.rigidbody)
                    {
                        // Apply force to the target object at the position of the hit point
                        Vector3 force = this.transform.forward * (this.forcePerSecond / this.frequency);
                        hitInfo.rigidbody.AddForceAtPosition(force, hitInfo.point, ForceMode.Impulse);
                    }
                    // Ricochet sound（复用枪上的 AudioSource，避免每次命中都新建/销毁临时 GameObject）
                    AudioClip sound = MaterialImpactManager.GetBulletHitSound(hitInfo.collider.sharedMaterial);
                    if (sound != null)
                    {
                        if (this.fireAudio != null) this.fireAudio.PlayOneShot(sound, this.hitSoundVolume);
                        else AudioSource.PlayClipAtPoint(sound, hitInfo.point, this.hitSoundVolume);
                    }
                    bullet.dist = hitInfo.distance;
                }
                else
                {
                    bullet.dist = 1000;
                }
            }
        }
    }

    public virtual void OnStartFire()
    {
        if (Time.timeScale == 0)
        {
            return;
        }
        this.firing = true;
        this.ShowMuzzleFlash();
        if (this.fireAudio != null)
        {
            this.fireAudio.Play();
        }
    }

    public virtual void OnStopFire()
    {
        this.firing = false;
        this.HideMuzzleFlash();
        if (this.fireAudio != null)
        {
            this.fireAudio.Stop();
        }
    }

    /// <summary>按住扳机时按开火频率逐发闪光，而不是一直亮着。</summary>
    void ShowMuzzleFlash()
    {
        if (this.muzzleFlashFront == null)
        {
            return;
        }
        if (!this.muzzleFlashFront.activeSelf)
        {
            this.muzzleFlashFront.SetActive(true);
        }
        this.muzzleFlashHideTime = Time.time + Mathf.Max(0.01f, this.muzzleFlashDuration);
    }

    void HideMuzzleFlash()
    {
        this.muzzleFlashHideTime = -1f;
        if (this.muzzleFlashFront != null && this.muzzleFlashFront.activeSelf)
        {
            this.muzzleFlashFront.SetActive(false);
        }
    }

    public AutoFire()
    {
        this.frequency = 10;
        this.coneAngle = 1.5f;
        this.damagePerSecond = 20f;
        this.forcePerSecond = 20f;
        this.hitSoundVolume = 0.5f;
        this.lastFireTime = -1;
    }

}