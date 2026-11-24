using UnityEngine;

/// <summary>
/// 一次性 3D 音效播放器：用固定数量的隐藏 AudioSource 轮转复用。
/// <para>
/// 替代 <see cref="AudioSource.PlayClipAtPoint"/>——后者每次调用都会新建一个临时 GameObject
/// 并在播放结束后销毁，拾取物 / 爆炸 / 命中等高频路径会产生持续的 GC 与实例化开销。
/// </para>
/// </summary>
public static class AudioOneShot
{
	const int VoiceCount = 8;
	const string HostName = "[AudioOneShot]";

	static AudioSource[] voices;
	static int nextIndex;

	public static void Play(AudioClip clip, Vector3 position, float volume = 1f)
	{
		if (clip == null) return;

		var source = Next();
		if (source == null) return;

		source.transform.position = position;
		source.PlayOneShot(clip, Mathf.Clamp01(volume));
	}

	static AudioSource Next()
	{
		if (voices == null) Create();

		if (voices == null) return null;

		// 优先找空闲的；都忙就轮转覆盖最早的那个
		for (int i = 0; i < voices.Length; i++)
		{
			int index = (nextIndex + i) % voices.Length;
			var candidate = voices[index];
			if (candidate == null) continue;
			if (candidate.isPlaying) continue;

			nextIndex = (index + 1) % voices.Length;
			return candidate;
		}

		var fallback = voices[nextIndex];
		nextIndex = (nextIndex + 1) % voices.Length;
		return fallback;
	}

	static void Create()
	{
		var host = new GameObject(HostName);
		Object.DontDestroyOnLoad(host);

		voices = new AudioSource[VoiceCount];
		for (int i = 0; i < VoiceCount; i++)
		{
			var source = host.AddComponent<AudioSource>();
			source.playOnAwake = false;
			source.spatialBlend = 1f;
			source.rolloffMode = AudioRolloffMode.Logarithmic;
			source.minDistance = 2f;
			source.maxDistance = 60f;
			voices[i] = source;
		}
		nextIndex = 0;
	}
}
