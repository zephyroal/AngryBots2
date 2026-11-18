using UnityEngine;

[System.Serializable]
public partial class FanRotate : MonoBehaviour
{
    [Tooltip("每帧滚动的 UV 偏移量（保留原实现按帧递增的行为）。")]
    public float scrollPerFrame = 0.25f;

    Material runtimeMaterial;

    public virtual void Start()
    {
        // 用贴图偏移代替改写网格 UV：原来的做法每帧把整个 UV 数组写回网格，
        // 会触发顶点缓冲重传，并且强制网格保持 Read/Write（内存翻倍）。
        var target = GetComponent<Renderer>();
        if (target != null) runtimeMaterial = target.material;
    }

    public virtual void Update()
    {
        if (runtimeMaterial == null) return;

        Vector2 offset = runtimeMaterial.mainTextureOffset;
        offset.y += scrollPerFrame;
        runtimeMaterial.mainTextureOffset = offset;
    }
}
