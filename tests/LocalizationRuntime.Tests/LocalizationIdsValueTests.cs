using System;
using System.Collections.Generic;
using System.Linq;
using LocalizationRuntime.Tests.Localization;
using Xunit;

namespace LocalizationRuntime.Tests
{
    /// <summary>
    /// 逐项核对每个常量取到的值，用的是**硬编码的期望表**。
    ///
    /// 为什么必须硬编码：如果期望值从 Catalog.Keys 推导，而 Catalog.Keys 与 id
    /// 由同一份排序结果产生，那么排序错了两边也一致，测试永远通过。
    ///
    /// 为什么测试资源里每个值都带唯一前缀（vNN-...）：
    /// 早先的值大量重复（"1"、"2"、"a"），一旦 id 整体错位，取到的值可能碰巧相同，
    /// 测试就抓不住。现在每个叶子值全局唯一，任何错位都会暴露。
    /// </summary>
    public sealed class LocalizationIdsValueTests
    {
        private static Localizer CreateZh()
        {
            return new Localizer(Catalog.Instance, System.Globalization.CultureInfo.GetCultureInfo("zh-CN"));
        }

        [Fact]
        public void ScalarValue()
        {
            Assert.Equal("v01-scalar", CreateZh().Get(LocalizationIds.Scalar));
        }

        [Fact]
        public void PlainObjectValues()
        {
            var l = CreateZh();

            Assert.Equal("v03-group-a", l.Get(LocalizationIds.GroupObject.A));
            Assert.Equal("v05-deep-b", l.Get(LocalizationIds.GroupObject.DeepObject.B));
            Assert.Equal("v06-deep-c-d", l.Get(LocalizationIds.GroupObject.DeepObject.CObject.D));
        }

        [Fact]
        public void DeeplyNestedObjectValues()
        {
            var l = CreateZh();

            Assert.Equal("v36-obwithobj-l1-l2-leaf",
                l.Get(LocalizationIds.ObjWithObjObject.Level1Object.Level2Object.Leaf));
            Assert.Equal("v37-obwithobj-l1-sibling",
                l.Get(LocalizationIds.ObjWithObjObject.Level1Object.Sibling));
        }

        [Fact]
        public void StringArrayValues()
        {
            var l = CreateZh();

            Assert.Equal("v08-strarr-0", l.Get(LocalizationIds.StrArrObject.StrArrItem0));
            Assert.Equal("v09-strarr-1", l.Get(LocalizationIds.StrArrObject.StrArrItem1));
            Assert.Equal("v10-strarr-2", l.Get(LocalizationIds.StrArrObject.StrArrItem2));
        }

        [Fact]
        public void SingleElementArrayValue()
        {
            Assert.Equal("v11-onestrarr-0",
                CreateZh().Get(LocalizationIds.OneStrArrObject.OneStrArrItem0));
        }

        [Fact]
        public void ObjectArrayValues()
        {
            var l = CreateZh();

            Assert.Equal("v12-objarr-0-k", l.Get(LocalizationIds.ObjArrObject.ObjArrItem0Object.K));
            Assert.Equal("v13-objarr-1-k", l.Get(LocalizationIds.ObjArrObject.ObjArrItem1Object.K));
        }

        [Fact]
        public void NestedStringArrayValues()
        {
            var l = CreateZh();

            Assert.Equal("v14-arrarr-0-0",
                l.Get(LocalizationIds.ArrArrObject.ArrArrItem0Object.ArrArrItem0Item0));
            Assert.Equal("v15-arrarr-0-1",
                l.Get(LocalizationIds.ArrArrObject.ArrArrItem0Object.ArrArrItem0Item1));
            Assert.Equal("v16-arrarr-1-0",
                l.Get(LocalizationIds.ArrArrObject.ArrArrItem1Object.ArrArrItem1Item0));
        }

        [Fact]
        public void NestedObjectArrayValues()
        {
            var l = CreateZh();

            Assert.Equal("v17-objarrarr-0-0-v", l.Get(
                LocalizationIds.ObjArrArrObject.ObjArrArrItem0Object.ObjArrArrItem0Item0Object.V));
            Assert.Equal("v18-objarrarr-0-1-v", l.Get(
                LocalizationIds.ObjArrArrObject.ObjArrArrItem0Object.ObjArrArrItem0Item1Object.V));
            Assert.Equal("v19-objarrarr-1-0-v", l.Get(
                LocalizationIds.ObjArrArrObject.ObjArrArrItem1Object.ObjArrArrItem1Item0Object.V));
        }

        [Fact]
        public void ArrayInsideObjectElementValues()
        {
            var l = CreateZh();

            Assert.Equal("v20-arrobj-0-sub-0",
                l.Get(LocalizationIds.ArrObjObject.ArrObjItem0Object.SubObject.SubItem0));
            Assert.Equal("v21-arrobj-0-sub-1",
                l.Get(LocalizationIds.ArrObjObject.ArrObjItem0Object.SubObject.SubItem1));
            Assert.Equal("v22-arobj-1-sub-0",
                l.Get(LocalizationIds.ArrObjObject.ArrObjItem1Object.SubObject.SubItem0));
        }

        [Fact]
        public void ThreeDimensionalArrayValues()
        {
            var l = CreateZh();

            Assert.Equal("v23-cube-0-0-0", l.Get(
                LocalizationIds.CubeObject.CubeItem0Object.CubeItem0Item0Object.CubeItem0Item0Item0));
            Assert.Equal("v24-cube-0-1-0", l.Get(
                LocalizationIds.CubeObject.CubeItem0Object.CubeItem0Item1Object.CubeItem0Item1Item0));
            Assert.Equal("v25-cube-1-0-0", l.Get(
                LocalizationIds.CubeObject.CubeItem1Object.CubeItem1Item0Object.CubeItem1Item0Item0));
        }

        [Fact]
        public void GridValues()
        {
            var l = CreateZh();

            Assert.Equal("v26-grid-0-0-h", l.Get(LocalizationIds.GridObject.GridItem0Object.GridItem0Item0Object.H));
            Assert.Equal("v27-grid-0-0-w", l.Get(LocalizationIds.GridObject.GridItem0Object.GridItem0Item0Object.W));
            Assert.Equal("v28-grid-1-0-h", l.Get(LocalizationIds.GridObject.GridItem1Object.GridItem1Item0Object.H));
            Assert.Equal("v29-grid-1-0-w", l.Get(LocalizationIds.GridObject.GridItem1Object.GridItem1Item0Object.W));
        }

        [Fact]
        public void DeepObjectArrayValues()
        {
            var l = CreateZh();

            Assert.Equal("v30-deepobjarr-0-name", l.Get(LocalizationIds.DeepObjArrObject.DeepObjArrItem0Object.Name));

            Assert.Equal("v31-deepobjarr-0-inner-0-x", l.Get(
                LocalizationIds.DeepObjArrObject.DeepObjArrItem0Object.InnerObject.InnerItem0Object.X));
            Assert.Equal("v32-deepobjarr-0-inner-0-y", l.Get(
                LocalizationIds.DeepObjArrObject.DeepObjArrItem0Object.InnerObject.InnerItem0Object.Y));
            Assert.Equal("v33-deepobjarr-0-inner-1-x", l.Get(
                LocalizationIds.DeepObjArrObject.DeepObjArrItem0Object.InnerObject.InnerItem1Object.X));
            Assert.Equal("v34-deepobjarr-0-inner-1-y", l.Get(
                LocalizationIds.DeepObjArrObject.DeepObjArrItem0Object.InnerObject.InnerItem1Object.Y));
        }

        /// <summary>
        /// 全局唯一性检查：所有叶子值必须互不相同。
        /// 这样任何 id 错位都会在逐项断言里暴露，而不是碰巧取到相同的值。
        /// </summary>
        [Fact]
        public void AllLeafValuesAreGloballyUnique()
        {
            var localizer = CreateZh();
            var leafCount = Catalog.Instance.Keys.Count;

            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            var duplicates = new List<string>();

            for (var id = 0; id < leafCount; id++)
            {
                var value = localizer.Get(id);

                int firstId;
                if (seen.TryGetValue(value, out firstId))
                {
                    duplicates.Add("值 '" + value + "' 同时出现在 id " + firstId + " 和 " + id);
                }
                else
                {
                    seen[value] = id;
                }
            }

            Assert.True(
                duplicates.Count == 0,
                "测试资源的值必须唯一才能验证 id 正确性：" + Environment.NewLine +
                string.Join(Environment.NewLine, duplicates));
        }

        /// <summary>zh-CN.json 的叶子总数。改动测试资源时必须同步更新。</summary>
        private const int ExpectedLeafCount = 33;

        [Fact]
        public void LeafCountMatchesResource()
        {
            Assert.Equal(ExpectedLeafCount, Catalog.Instance.Keys.Count);
        }
    }
}

