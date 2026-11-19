using System.IO;
using UnityEditor;
using UnityEngine;

namespace ABGameplay
{
	/// <summary>
	/// 生成 / 刷新按关卡权重的增益掉落表（Assets/Resources/PowerUpDropTable.asset）。
	/// 用法：-executeMethod ABGameplay.PowerUpSetup.Run
	/// </summary>
	public static class PowerUpSetup
	{
		const string ResourcesDir = "Assets/Resources";
		const string TablePath = ResourcesDir + "/PowerUpDropTable.asset";

		[MenuItem("ABGameplay/19. Create PowerUp Drop Table")]
		public static void Run()
		{
			if (!AssetDatabase.IsValidFolder(ResourcesDir))
			{
				if (!Directory.Exists(ResourcesDir)) Directory.CreateDirectory(ResourcesDir);
				AssetDatabase.Refresh();
			}

			var table = AssetDatabase.LoadAssetAtPath<PowerUpDropTable>(TablePath);
			bool created = false;
			if (table == null)
			{
				table = ScriptableObject.CreateInstance<PowerUpDropTable>();
				AssetDatabase.CreateAsset(table, TablePath);
				created = true;
			}

			table.rows.Clear();
			table.rows.Add(new PowerUpDropTable.Row
			{
				match = "boss",
				damageWeight = 0.5f,
				rapidWeight = 1f,
				shieldWeight = 3f,
			});
			table.rows.Add(new PowerUpDropTable.Row
			{
				match = "defend",
				damageWeight = 0.5f,
				rapidWeight = 1f,
				shieldWeight = 2.5f,
			});
			table.rows.Add(new PowerUpDropTable.Row
			{
				match = "escape",
				damageWeight = 1f,
				rapidWeight = 2f,
				shieldWeight = 1f,
			});
			table.defaultWeights = new[] { 1f, 1f, 1f };

			EditorUtility.SetDirty(table);
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();

			Debug.Log("[PowerUpSetup] " + (created ? "CREATED " : "UPDATED ") + TablePath
				+ " rows=" + table.rows.Count);
			EditorApplication.Exit(0);
		}
	}
}
