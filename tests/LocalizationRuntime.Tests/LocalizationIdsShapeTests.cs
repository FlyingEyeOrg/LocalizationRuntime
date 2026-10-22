using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LocalizationRuntime.Tests.Localization;
using Xunit;

namespace LocalizationRuntime.Tests
{
    /// <summary>
    /// 逐种 JSON 形状验证生成结果。
    ///
    /// 之前的测试只做全局性质断言（唯一、连续），
    /// 一旦生成器漏掉某一类节点，只要编号仍然连续就查不出来。
    /// 这里对每个形状给出明确的期望结构。
    /// </summary>
    public sealed class LocalizationIdsShapeTests
    {
        private static readonly Type Root = typeof(LocalizationIds);

        /// <summary>按路径查找某个嵌套类。</summary>
        private static Type? FindType(string path)
        {
            var type = Root;
            foreach (var part in path.Split('.'))
            {
                type = type?.GetNestedType(part, BindingFlags.Public | BindingFlags.Static);
            }

            return type;
        }

        private static bool HasConst(Type? type, string name)
        {
            if (type == null)
            {
                return false;
            }

            var field = type.GetField(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            return field != null && field.IsLiteral && field.FieldType == typeof(int);
        }

        private static void AssertConst(Type? type, string name, string context)
        {
            Assert.True(HasConst(type, name), context + " 缺少常量 " + name);
        }

        private static void AssertClass(string path)
        {
            Assert.True(FindType(path) != null, "缺少类 " + path);
        }

        // ── ① 标量：只有叶子，无类 ──
        [Fact]
        public void Scalar_HasLeafConstantOnly()
        {
            AssertConst(Root, "Scalar", "根");
            Assert.True(FindType("ScalarObject") == null, "标量不应生成类");
        }

        // ── ② 普通对象：常量 + Object 类 ──
        [Fact]
        public void PlainObject_HasBothIdAndObjectClass()
        {
            AssertConst(Root, "Group", "根");
            AssertClass("GroupObject");
        }

        // ── ③ 三级嵌套对象：每一层都要有 id 与类 ──
        [Fact]
        public void NestedObjects_EveryLevelHasIdAndClass()
        {
            AssertConst(Root, "ObjWithObj", "根");
            AssertClass("ObjWithObjObject");

            var level1 = FindType("ObjWithObjObject");
            AssertConst(level1, "Level1", "ObjWithObjObject");
            AssertClass("ObjWithObjObject.Level1Object");

            var level2 = FindType("ObjWithObjObject.Level1Object");
            AssertConst(level2, "Level2", "Level1Object");
            AssertConst(level2, "Sibling", "Level1Object");
            AssertClass("ObjWithObjObject.Level1Object.Level2Object");
            AssertConst(FindType("ObjWithObjObject.Level1Object.Level2Object"), "Leaf", "Level2Object");
        }

        // ── ④ 字符串数组 ──
        [Fact]
        public void StringArray_HasContainerIdClassAndItemConstants()
        {
            AssertConst(Root, "StrArr", "根");
            AssertClass("StrArrObject");

            var cls = FindType("StrArrObject");
            AssertConst(cls, "StrArrItem0", "StrArrObject");
            AssertConst(cls, "StrArrItem1", "StrArrObject");
            AssertConst(cls, "StrArrItem2", "StrArrObject");
            Assert.True(!HasConst(cls, "StrArrItem3"), "StrArr 只有 3 个元素，不应存在 Item3");
        }

        // ── ⑤ 单元素数组 ──
        [Fact]
        public void SingleElementArray_IsHandled()
        {
            AssertConst(Root, "OneStrArr", "根");
            AssertClass("OneStrArrObject");
            AssertConst(FindType("OneStrArrObject"), "OneStrArrItem0", "OneStrArrObject");
        }

        // ── ⑥ 对象数组 ──
        [Fact]
        public void ObjectArray_EachElementHasIdAndClass()
        {
            AssertConst(Root, "ObjArr", "根");
            AssertClass("ObjArrObject");

            var arrayClass = FindType("ObjArrObject");
            AssertConst(arrayClass, "ObjArrItem0", "ObjArrObject");
            AssertConst(arrayClass, "ObjArrItem1", "ObjArrObject");

            AssertClass("ObjArrObject.ObjArrItem0Object");
            AssertClass("ObjArrObject.ObjArrItem1Object");
            AssertConst(FindType("ObjArrObject.ObjArrItem0Object"), "K", "ObjArrItem0Object");
        }

        // ── ⑦ 二维字符串数组 ──
        [Fact]
        public void NestedStringArray_KeepsItemAtEveryLevel()
        {
            AssertConst(Root, "ArrArr", "根");
            AssertClass("ArrArrObject");

            var outer = FindType("ArrArrObject");
            AssertConst(outer, "ArrArrItem0", "ArrArrObject");
            AssertConst(outer, "ArrArrItem1", "ArrArrObject");

            AssertClass("ArrArrObject.ArrArrItem0Object");
            var inner = FindType("ArrArrObject.ArrArrItem0Object");
            AssertConst(inner, "ArrArrItem0Item0", "ArrArrItem0Object");
            AssertConst(inner, "ArrArrItem0Item1", "ArrArrItem0Object");
        }

        // ── ⑧ 二维对象数组 ──
        [Fact]
        public void NestedObjectArray_UsesItemItemNaming()
        {
            AssertConst(Root, "ObjArrArr", "根");

            var leaf = FindType("ObjArrArrObject.ObjArrArrItem0Object.ObjArrArrItem0Item0Object");
            Assert.NotNull(leaf);
            AssertConst(leaf, "V", "ObjArrArrItem0Item0Object");
        }

        // ── ⑨ 对象元素里套数组 ──
        [Fact]
        public void ArrayInsideObjectElement_IsGenerated()
        {
            AssertConst(Root, "ArrObj", "根");

            var element = FindType("ArrObjObject.ArrObjItem0Object");
            Assert.NotNull(element);
            AssertConst(element, "Sub", "ArrObjItem0Object");
            AssertClass("ArrObjObject.ArrObjItem0Object.SubObject");

            var sub = FindType("ArrObjObject.ArrObjItem0Object.SubObject");
            AssertConst(sub, "SubItem0", "SubObject");
            AssertConst(sub, "SubItem1", "SubObject");
        }

        // ── ⑩ 三维数组 ──
        [Fact]
        public void ThreeDimensionalArray_IsGenerated()
        {
            AssertConst(Root, "Cube", "根");

            var a = FindType("CubeObject");
            Assert.NotNull(a);
            AssertConst(a, "CubeItem0", "CubeObject");

            var b = FindType("CubeObject.CubeItem0Object");
            Assert.NotNull(b);
            AssertConst(b, "CubeItem0Item0", "CubeItem0Object");

            var c = FindType("CubeObject.CubeItem0Object.CubeItem0Item0Object");
            Assert.NotNull(c);
            AssertConst(c, "CubeItem0Item0Item0", "CubeItem0Item0Object");
        }

        // ── ⑪ 二维对象数组（对象里两个字段） ──
        [Fact]
        public void Grid_EveryCellHasBothFields()
        {
            AssertConst(Root, "Grid", "根");

            var cell = FindType("GridObject.GridItem0Object.GridItem0Item0Object");
            Assert.NotNull(cell);
            AssertConst(cell, "H", "GridItem0Item0Object");
            AssertConst(cell, "W", "GridItem0Item0Object");
        }

        // ── ⑫ 对象数组 → 元素对象 → 元素内数组 → 数组内对象 ──
        [Fact]
        public void DeeplyNestedCombination_IsGenerated()
        {
            AssertConst(Root, "DeepObjArr", "根");

            var element = FindType("DeepObjArrObject.DeepObjArrItem0Object");
            Assert.NotNull(element);
            AssertConst(element, "Name", "DeepObjArrItem0Object");
            AssertConst(element, "Inner", "DeepObjArrItem0Object");

            var innerCell = FindType(
                "DeepObjArrObject.DeepObjArrItem0Object.InnerObject.InnerItem1Object");
            Assert.NotNull(innerCell);
            AssertConst(innerCell, "X", "InnerItem1Object");
            AssertConst(innerCell, "Y", "InnerItem1Object");
        }
    }
}
