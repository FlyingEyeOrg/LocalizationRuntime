using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LocalizationRuntime.Tests.Localization;
using Xunit;

namespace LocalizationRuntime.Tests
{
    /// <summary>
    /// 容器（对象 / 数组）的取值能力。
    ///
    /// 容器有 id 不只是当标签：必须能由它拿到子 id、元素个数和整段文本，
    /// 否则「取整个数组」这件事做不到。
    /// </summary>
    public sealed class LocalizationContainerTests
    {
        /// <summary>构造一个 zh-CN 的 localizer。</summary>
        private static Localizer Zh()
        {
            return new Localizer(Catalog.Instance, CultureInfo.GetCultureInfo("zh-CN"));
        }

        // ── 元素个数 ──

        [Fact]
        public void GetChildCount_ReturnsArrayLength()
        {
            var l = Zh();

            Assert.Equal(3, l.GetChildCount(LocalizationIds.StrArr));
            Assert.Equal(1, l.GetChildCount(LocalizationIds.OneStrArr));
            Assert.Equal(2, l.GetChildCount(LocalizationIds.ObjArr));
            Assert.Equal(2, l.GetChildCount(LocalizationIds.ArrArr));
        }

        [Fact]
        public void GetChildCount_ReturnsZeroForLeaf()
        {
            var l = Zh();
            Assert.Equal(0, l.GetChildCount(LocalizationIds.Scalar));
        }

        // ── 子 id ──

        [Fact]
        public void GetChildren_ReturnsElementIdsInOrder()
        {
            var l = Zh();
            var children = l.GetChildren(LocalizationIds.StrArr);

            Assert.Equal(3, children.Length);
            Assert.Equal(LocalizationIds.StrArrObject.StrArrItem0, children[0]);
            Assert.Equal(LocalizationIds.StrArrObject.StrArrItem1, children[1]);
            Assert.Equal(LocalizationIds.StrArrObject.StrArrItem2, children[2]);
        }

        [Fact]
        public void GetChildren_OfObjectArray_ReturnsElementContainers()
        {
            var l = Zh();
            var children = l.GetChildren(LocalizationIds.ObjArr);

            Assert.Equal(2, children.Length);
            Assert.Equal(LocalizationIds.ObjArrObject.ObjArrItem0, children[0]);
            Assert.Equal(LocalizationIds.ObjArrObject.ObjArrItem1, children[1]);

            // 元素本身是容器，每个有 1 个成员 K
            Assert.Equal(1, l.GetChildCount(children[0]));
        }

        [Fact]
        public void GetChildren_OfNestedArray_StaysHierarchical()
        {
            var l = Zh();

            var rows = l.GetChildren(LocalizationIds.ArrArr);
            Assert.Equal(2, rows.Length);

            // 行 0 有两个元素，行 1 有一个
            Assert.Equal(2, l.GetChildCount(rows[0]));
            Assert.Equal(1, l.GetChildCount(rows[1]));
        }

        [Fact]
        public void GetChildren_OfPlainObject_ReturnsMemberIds()
        {
            var l = Zh();
            var members = l.GetChildren(LocalizationIds.GroupObject.A == 0 ? LocalizationIds.Group : LocalizationIds.Group);

            // Group 有 A 和 Deep 两个成员，都能取值或继续展开
            Assert.Equal(2, members.Length);
            Assert.Contains(LocalizationIds.GroupObject.A, members);
        }

        [Fact]
        public void GetChildren_OfLeaf_IsEmpty()
        {
            var l = Zh();
            Assert.Empty(l.GetChildren(LocalizationIds.Scalar));
        }

        [Fact]
        public void GetChildren_OutOfRange_IsEmpty()
        {
            var l = Zh();
            Assert.Empty(l.GetChildren(999999));
            Assert.Empty(l.GetChildren(-1));
        }

        // ── 取整段文本 ──

        [Fact]
        public void GetValues_ReturnsAllStringsOfArray()
        {
            var l = Zh();
            var values = l.GetValues(LocalizationIds.StrArr);

            Assert.Equal(new[] { "v08-strarr-0", "v09-strarr-1", "v10-strarr-2" }, values);
        }

        [Fact]
        public void GetValues_OfNestedArrayRow()
        {
            var l = Zh();
            var rows = l.GetChildren(LocalizationIds.ArrArr);

            Assert.Equal(new[] { "v14-arrarr-0-0", "v15-arrarr-0-1" }, l.GetValues(rows[0]));
            Assert.Equal(new[] { "v16-arrarr-1-0" }, l.GetValues(rows[1]));
        }

        [Fact]
        public void GetValues_OfObjectArray_Throws()
        {
            var l = Zh();
            // 元素是对象，没有直接文本；应引导调用方改用 GetChildren。
            var ex = Assert.Throws<InvalidOperationException>(
                () => l.GetValues(LocalizationIds.ObjArr));

            Assert.Contains("GetChildren", ex.Message);
        }

        [Fact]
        public void GetValues_OfLeaf_IsEmpty()
        {
            var l = Zh();
            Assert.Empty(l.GetValues(LocalizationIds.Scalar));
        }

        // ── 拍平 ──

        [Fact]
        public void GetAllValues_FlattensNestedArray()
        {
            var l = Zh();
            var all = l.GetAllValues(LocalizationIds.ArrArr);

            Assert.Equal(
                new[] { "v14-arrarr-0-0", "v15-arrarr-0-1", "v16-arrarr-1-0" },
                all);
        }

        [Fact]
        public void GetAllValues_FlattensThreeDimensionalArray()
        {
            var l = Zh();
            var all = l.GetAllValues(LocalizationIds.Cube);

            Assert.Equal(
                new[] { "v23-cube-0-0-0", "v24-cube-0-1-0", "v25-cube-1-0-0" },
                all);
        }

        [Fact]
        public void GetAllValues_FlattensObjectArray()
        {
            var l = Zh();
            var all = l.GetAllValues(LocalizationIds.ObjArr);

            Assert.Equal(new[] { "v12-objarr-0-k", "v13-objarr-1-k" }, all);
        }

        [Fact]
        public void GetAllValues_IsStableAndCoversEveryLeaf()
        {
            var l = Zh();
            var all = l.GetAllValues(LocalizationIds.DeepObjArr);

            // 对象成员的顺序由键的排序决定（与生成的常量顺序一致），
            // 不保证等于 JSON 原文顺序；但必须是确定的、且不漏元素。
            Assert.Equal(5, all.Length);
            Assert.Contains("v30-deepobjarr-0-name", all);
            Assert.Contains("v31-deepobjarr-0-inner-0-x", all);
            Assert.Contains("v32-deepobjarr-0-inner-0-y", all);
            Assert.Contains("v33-deepobjarr-0-inner-1-x", all);
            Assert.Contains("v34-deepobjarr-0-inner-1-y", all);

            // 同一实例内重复调用结果一致
            Assert.Equal(all, l.GetAllValues(LocalizationIds.DeepObjArr));
        }

        [Fact]
        public void GetAllValues_OfArray_PreservesElementOrder()
        {
            var l = Zh();

            Assert.Equal(
                new[] { "v14-arrarr-0-0", "v15-arrarr-0-1", "v16-arrarr-1-0" },
                l.GetAllValues(LocalizationIds.ArrArr));

            Assert.Equal(
                new[] { "v08-strarr-0", "v09-strarr-1", "v10-strarr-2" },
                l.GetAllValues(LocalizationIds.StrArr));
        }

        // ── 与叶子 API 的一致性 ──

        [Fact]
        public void EveryChildIdOfArrayIsReadable()
        {
            var l = Zh();

            foreach (var arrayId in new[]
                     {
                         LocalizationIds.StrArr, LocalizationIds.OneStrArr,
                         LocalizationIds.ArrArr, LocalizationIds.Cube,
                         LocalizationIds.ObjArr, LocalizationIds.ObjArrArr,
                         LocalizationIds.ArrObj, LocalizationIds.Grid,
                         LocalizationIds.DeepObjArr,
                     })
            {
                foreach (var childId in l.GetChildren(arrayId))
                {
                    Assert.NotEqual(0, l.GetChildCount(childId) + (childId < LocalizationIds.TextCount ? 1 : 0));

                    // 子 id 要么可取文本，要么可继续展开，不能两者皆无
                    var isLeaf = childId < LocalizationIds.TextCount;
                    if (isLeaf)
                    {
                        Assert.False(string.IsNullOrEmpty(l.Get(childId)));
                    }
                    else
                    {
                        Assert.NotEmpty(l.GetChildren(childId));
                    }
                }
            }
        }

        [Fact]
        public void GetValues_AgreesWithGetById()
        {
            var l = Zh();

            var values = l.GetValues(LocalizationIds.StrArr);
            var children = l.GetChildren(LocalizationIds.StrArr);

            for (var i = 0; i < children.Length; i++)
            {
                Assert.Equal(l.Get(children[i]), values[i]);
            }
        }

        [Fact]
        public void ContainerApis_FollowCultureSwitch()
        {
            var l = Zh();
            var localizer = new Localizer(Catalog.Instance, CultureInfo.GetCultureInfo("zh-CN"));
            var before = l.GetValues(LocalizationIds.StrArr);

            l.SetCulture(CultureInfo.GetCultureInfo("en-US"));
            var after = l.GetValues(LocalizationIds.StrArr);

            Assert.Equal(new[] { "v08-strarr-0", "v09-strarr-1", "v10-strarr-2" }, before);
            Assert.Equal(new[] { "e08-strarr-0", "e09-strarr-1", "e10-strarr-2" }, after);
        }

        [Fact]
        public void FlattenedCount_MatchesTextCountOfSubtree()
        {
            var l = Zh();

            // Cube 有 3 个叶子，拍平后正好 3 个
            Assert.Equal(3, l.GetAllValues(LocalizationIds.Cube).Length);

            // Grid 有 4 个叶子（2 行 × 每行 1 个格子 × 2 字段）
            Assert.Equal(4, l.GetAllValues(LocalizationIds.Grid).Length);
        }
    }
}








