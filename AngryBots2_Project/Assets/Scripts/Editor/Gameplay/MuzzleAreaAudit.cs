using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ABGameplay
{
	/// <summary>
	/// 玩家周围渲染器审计：列出玩家 6 米内的所有 Renderer 及其材质/shader/混合方式，
	/// 用于定位「枪口附近一块黑色方块」这类来源不明的半透明面片。
	/// 用法：-executeMethod ABGameplay.MuzzleAreaAudit.Run
	/// </summary>
	public static class MuzzleAreaAudit
	{
		static readonly string[] Scenes =
		{
			"Assets/AngryBots.unity",
			"Assets/U6Port/Scenes/AngryBots.unity",
			"Assets/Scenes/Scenes/_CompletedLevel.unity",
		};

		const float Radius = 6f;

		[MenuItem("ABGameplay/16. Muzzle Area Audit")]
		public static void Run()
		{
			foreach (var path in Scenes)
			{
				var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
				if (!scene.IsValid()) continue;

				Transform player = null;
				var tagged = GameObject.FindGameObjectWithTag("Player");
				if (tagged != null) player = tagged.transform;
				if (player == null)
				{
					var health = Object.FindObjectOfType<PlayerHealth>(true);
					if (health != null) player = health.transform;
				}
				if (player == null)
				{
					Debug.LogWarning("[AreaAudit] " + Path.GetFileName(path) + " 找不到玩家");
					continue;
				}

				Debug.Log("[AreaAudit] SCENE " + Path.GetFileName(path) + " player=" + player.name + " pos=" + player.position);

				int listed = 0;
				foreach (var renderer in Object.FindObjectsOfType<Renderer>(true))
				{
					if (renderer == null || renderer.transform.IsChildOf(player)) continue;
					if (Vector3.Distance(renderer.bounds.center, player.position) > Radius) continue;

					var material = renderer.sharedMaterial;
					string shaderName = material != null && material.shader != null ? material.shader.name : "null";
					int queue = material != null ? material.renderQueue : -1;

					bool interesting = shaderName.Contains("Multiply")
						|| shaderName.Contains("Blend")
						|| shaderName.Contains("Refract")
						|| shaderName.Contains("Additive")
						|| queue >= 3000;
					if (!interesting) continue;

					listed++;
					Debug.Log("[AreaAudit]   " + renderer.GetType().Name + " " + renderer.gameObject.name
						+ " | mat=" + (material != null ? material.name : "null")
						+ " | shader=" + shaderName
						+ " | queue=" + queue
						+ " | size=" + renderer.bounds.size.ToString("0.00")
						+ " | pos=" + renderer.bounds.center.ToString("0.0"));
				}
				Debug.Log("[AreaAudit]   transparentOrFxRenderers=" + listed);
			}

			Debug.Log("[AreaAudit] DONE");
			EditorApplication.Exit(0);
		}
	}
}
