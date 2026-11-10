using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Streamer.Test
{ 
    /// <summary>
    /// 测试Inspector显示功能的脚本
    /// 在编辑器中自动填充大量随机数据用于性能测试
    /// 使用Unity自身机制显示，不依赖自定义Editor
    /// 
    /// Author: Zephyr.He
    /// Description: This script is designed to test the performance and display capabilities of Unity's Inspector window
    /// by generating large amounts of random data. It includes various data types and structures to comprehensively
    /// evaluate Inspector behavior under heavy load conditions.
    /// </summary>
    [ExecuteAlways]
    public class TestInspectorDisplay : MonoBehaviour
    {
        [Header("性能测试控制")]
        [Tooltip("在编辑器中自动填充大量随机数据")]
        public bool autoFillInEditor = true;
        
        [Tooltip("要生成的随机数据量")]
        public int randomDataCount = 2000;
        
        [Header("复杂列表类型 - 性能测试")]
        public List<Vector3> vector3List = new List<Vector3>();
        public List<Color> colorList = new List<Color>();
        public List<string> stringList = new List<string>();
        public List<float> floatList = new List<float>();
        public List<int> intList = new List<int>();
        
        [Header("嵌套结构")]
        public List<List<Vector2>> nestedVectorList = new List<List<Vector2>>();
        
        [Header("基础数据类型")]
        public int testInt = 42;
        public float testFloat = 3.14159f;
        public bool testBool = true;
        public string testString = "Hello World";
        public Vector2 testVector2 = new Vector2(1.0f, 2.0f);
        public Vector3 testVector3 = new Vector3(1.0f, 2.0f, 3.0f);
        public Vector4 testVector4 = new Vector4(1.0f, 2.0f, 3.0f, 4.0f);
        public Color testColor = Color.red;
        public AnimationCurve testCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        
        [Header("Unity对象引用")]
        public GameObject testGameObject;
        public Transform testTransform;
        public Material testMaterial;
        public Mesh testMesh;
        public Texture2D testTexture;
        public AudioClip testAudioClip;
        
        [Header("数组和列表")]
        public int[] intArray = new int[] { 1, 2, 3, 4, 5 };
        public float[] floatArray = new float[] { 1.1f, 2.2f, 3.3f, 4.4f, 5.5f };
        public string[] stringArray = new string[] { "Apple", "Banana", "Cherry", "Date", "Elderberry" };
        public Vector3[] vector3Array = new Vector3[] { 
            new Vector3(1, 1, 1), 
            new Vector3(2, 2, 2), 
            new Vector3(3, 3, 3) 
        };
        
        [Header("枚举和层")]
        public LayerMask testLayerMask = ~0;
        public TestEnum testEnum = TestEnum.Option2;
        
        [Header("范围和约束")]
        [Range(0, 100)]
        public int rangedInt = 50;
        
        [Range(0.0f, 1.0f)]
        public float rangedFloat = 0.75f;
        
        [Header("多行文本")]
        [Multiline(5)]
        public string multiLineText = "This is a multi-line text field.\nIt can contain multiple lines of text.\nUseful for longer descriptions.";
        
        [Header("文本区域")]
        [TextArea(3, 10)]
        public string textArea = "This is a text area.\nIt's designed for longer blocks of text.\nYou can resize it vertically.";
        
        [Header("工具提示")]
        [Tooltip("This is a tooltip that appears when you hover over the field")]
        public string fieldWithTooltip = "Hover over me to see the tooltip!";
        
        [Header("条件显示")]
        public bool showConditionalField = false;
        
        [Tooltip("This field is only visible when showConditionalField is true")]
        [SerializeField]
        private string conditionalField = "Conditional content";
        
        [Header("只读字段")]
        [ReadOnly]
        public string readOnlyField = "This field cannot be edited in the inspector";
        
        // 测试枚举
        public enum TestEnum
        {
            Option1,
            Option2,
            Option3,
            Option4
        }
        
        // 自定义只读属性Drawer的属性
        public class ReadOnlyAttribute : PropertyAttribute { }
        
#if UNITY_EDITOR
        void Update()
        {
            // 只在编辑器中运行，且不处于播放模式
            if (!Application.isPlaying && autoFillInEditor)
            {
                // 检查是否需要填充数据（列表为空时）
                if (vector3List.Count == 0 && randomDataCount > 0)
                {
                    FillWithRandomData();
                }
            }
        }
#endif
        
        /// <summary>
        /// 填充大量随机数据以测试Inspector性能
        /// </summary>
        private void FillWithRandomData()
        {
            Debug.Log($"开始填充 {randomDataCount} 个随机数据项...");
            
            System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();
            stopwatch.Start();
            
            // 填充Vector3列表
            vector3List.Clear();
            for (int i = 0; i < randomDataCount; i++)
            {
                vector3List.Add(new Vector3(
                    Random.Range(-100f, 100f),
                    Random.Range(-100f, 100f),
                    Random.Range(-100f, 100f)
                ));
            }
            
            // 填充Color列表
            colorList.Clear();
            for (int i = 0; i < randomDataCount; i++)
            {
                colorList.Add(new Color(
                    Random.Range(0f, 1f),
                    Random.Range(0f, 1f),
                    Random.Range(0f, 1f),
                    Random.Range(0f, 1f)
                ));
            }
            
            // 填充String列表
            stringList.Clear();
            for (int i = 0; i < randomDataCount; i++)
            {
                stringList.Add($"RandomString_{i}_{Random.Range(0, 10000)}");
            }
            
            // 填充Float列表
            floatList.Clear();
            for (int i = 0; i < randomDataCount; i++)
            {
                floatList.Add(Random.Range(-1000f, 1000f));
            }
            
            // 填充Int列表
            intList.Clear();
            for (int i = 0; i < randomDataCount; i++)
            {
                intList.Add(Random.Range(-10000, 10000));
            }
            
            // 填充嵌套Vector2列表
            nestedVectorList.Clear();
            for (int i = 0; i < Mathf.Min(100, randomDataCount / 20); i++)
            {
                List<Vector2> innerList = new List<Vector2>();
                int innerCount = Random.Range(5, 20);
                for (int j = 0; j < innerCount; j++)
                {
                    innerList.Add(new Vector2(
                        Random.Range(-50f, 50f),
                        Random.Range(-50f, 50f)
                    ));
                }
                nestedVectorList.Add(innerList);
            }
            
            stopwatch.Stop();
            Debug.Log($"填充完成，耗时: {stopwatch.ElapsedMilliseconds} ms");
            
#if UNITY_EDITOR
            // 标记对象为脏，确保Inspector更新
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }
    }
}