using UnityEngine;
using System.Collections;

[System.Serializable]
public partial class MuzzleFlashAnimate : MonoBehaviour
{
    [Tooltip("打开后每帧随机缩放/旋转（原 AngryBots 行为）；关闭时只在每次启用时随机一次，配合逐发闪光更好看。")]
    public bool animateContinuously;

    void OnEnable()
    {
        Randomize();
    }

    public virtual void Update()
    {
        if (this.animateContinuously)
        {
            Randomize();
        }
    }

    void Randomize()
    {
        this.transform.localScale = Vector3.one * Random.Range(0.8f, 1.25f);

        {
            float angle = Random.Range(0, 90f);
            Vector3 euler = this.transform.localEulerAngles;
            euler.z = angle;
            this.transform.localEulerAngles = euler;
        }
    }

}
