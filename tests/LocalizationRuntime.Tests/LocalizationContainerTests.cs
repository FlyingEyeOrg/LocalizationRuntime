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
        /// <summary>每个用例先把当前语言设为 zh-CN，然后直接用静态方法。</summary>
        private static void UseZh()
        {
            Localizer.Install(Catalog.Instance, CultureInfo.GetCultureInfo("zh-CN"));
        }

        // ── 元素个数 ──

        [Fact]
        public void GetChildCount_ReturnsArrayLength()
        {
            UseZh();

            Assert.Equal(3, Localizer.GetChildCount(LocalizationIds.StrArr));
            Assert.Equal(1, Localizer.GetChildCount(LocalizationIds.OneStrArr));
            Assert.Equal(2, Localizer.GetChildCount(LocalizationIds.ObjArr));
            Assert.Equal(2, Localizer.GetChildCount(LocalizationIds.ArrArr));
        }

        [Fact]
        public void GetChildCount_ReturnsZeroForLeaf()
        {
            Assert.Equal(0, Localizer.GetChildCount(LocalizationIds.Scalar));
        }

        // ── 子 id ──

        [Fact]
        public void GetChildren_ReturnsElementIdsInOrder()
        {
            UseZh();
            var children = Localizer.GetChildren(LocalizationIds.StrArr);

            Assert.Equal(3, children.Length);
            Assert.Equal(LocalizationIds.StrArrObject.StrArrItem0, children[0]);
            Assert.Equal(LocalizationIds.StrArrObject.StrArrItem1, children[1]);
            Assert.Equal(LocalizationIds.StrArrObject.StrArrItem2, children[2]);
        }

        [Fact]
        public void GetChildren_OfObjectArray_ReturnsElementContainers()
        {
            UseZh();
            var children = Localizer.GetChildren(LocalizationIds.ObjArr);

            Assert.Equal(2, children.Length);
            Assert.Equal(LocalizationIds.ObjArrObject.ObjArrItem0, children[0]);
            Assert.Equal(LocalizationIds.ObjArrObject.ObjArrItem1, children[1]);

            // 元素本身是容器，每个有 1 个成员 K
            Assert.Equal(1, Localizer.GetChildCount(children[0]));
        }

        [Fact]
        public void GetChildren_OfNestedArray_StaysHierarchical()
        {
            UseZh();

            var rows = Localizer.GetChildren(LocalizationIds.ArrArr);
            Assert.Equal(2, rows.Length);

            // 行 0 有两个元素，行 1 有一个
            Assert.Equal(2, Localizer.GetChildCount(rows[0]));
            Assert.Equal(1, Localizer.GetChildCount(rows[1]));
        }

        [Fact]
        public void GetChildren_OfPlainObject_ReturnsMemberIds()
        {
            UseZh();
            var members = Localizer.GetChildren(LocalizationIds.GroupObject.A == 0 ? LocalizationIds.Group : LocalizationIds.Group);

            // Group 有 A 和 Deep 两个成员，都能取值或继续展开
            Assert.Equal(2, members.Length);
            Assert.Contains(LocalizationIds.GroupObject.A, members);
        }

        [Fact]
        public void GetChildren_OfLeaf_IsEmpty()
        {
            Assert.Empty(Localizer.GetChildren(LocalizationIds.Scalar));
        }

        [Fact]
        public void GetChildren_OutOfRange_IsEmpty()
        {
            Assert.Empty(Localizer.GetChildren(999999));
            Assert.Empty(Localizer.GetChildren(-1));
        }

        // ── 取整段文本 ──

        [Fact]
        public void GetValues_ReturnsAllStringsOfArray()
        {
            var values = Localizer.GetValues(LocalizationIds.StrArr);

            Assert.Equal(new[] { "v08-strarr-0", "v09-strarr-1", "v10-strarr-2" }, values);
        }

        [Fact]
        public void GetValues_OfNestedArrayRow()
        {
            UseZh();
            var rows = Localizer.GetChildren(LocalizationIds.ArrArr);

            Assert.Equal(new[] { "v14-arrarr-0-0", "v15-arrarr-0-1" }, Localizer.GetValues(rows[0]));
            Assert.Equal(new[] { "v16-arrarr-1-0" }, Localizer.GetValues(rows[1]));
        }

        [Fact]
        public void GetValues_OfObjectArray_Throws()
        {
            // 元素是对象，没有直接文本；应引导调用方改用 GetChildren。
            var ex = Assert.Throws<InvalidOperationException>(
                () => Localizer.GetValues(LocalizationIds.ObjArr));

            Assert.Contains("GetChildren", ex.Message);
        }

        [Fact]
        public void GetValues_OfLeaf_IsEmpty()
        {
            Assert.Empty(Localizer.GetValues(LocalizationIds.Scalar));
        }

        // ── 拍平 ──

        [Fact]
        public void GetAllValues_FlattensNestedArray()
        {
            var all = Localizer.GetAllValues(LocalizationIds.ArrArr);

            Assert.Equal(
                new[] { "v14-arrarr-0-0", "v15-arrarr-0-1", "v16-arrarr-1-0" },
                all);
        }

        [Fact]
        public void GetAllValues_FlattensThreeDimensionalArray()
        {
            var all = Localizer.GetAllValues(LocalizationIds.Cube);

            Assert.Equal(
                new[] { "v23-cube-0-0-0", "v24-cube-0-1-0", "v25-cube-1-0-0" },
                all);
        }

        [Fact]
        public void GetAllValues_FlattensObjectArray()
        {
            var all = Localizer.GetAllValues(LocalizationIds.ObjArr);

            Assert.Equal(new[] { "v12-objarr-0-k", "v13-objarr-1-k" }, all);
        }

        [Fact]
        public void GetAllValues_IsStableAndCoversEveryLeaf()
        {
            UseZh();
            var all = Localizer.GetAllValues(LocalizationIds.DeepObjArr);

            // 对象成员的顺序由键的排序决定（与生成的常量顺序一致），
            // 不保证等于 JSON 原文顺序；但必须是确定的、且不漏元素。
            Assert.Equal(5, all.Length);
            Assert.Contains("v30-deepobjarr-0-name", all);
            Assert.Contains("v31-deepobjarr-0-inner-0-x", all);
            Assert.Contains("v32-deepobjarr-0-inner-0-y", all);
            Assert.Contains("v33-deepobjarr-0-inner-1-x", all);
            Assert.Contains("v34-deepobjarr-0-inner-1-y", all);

            // 同一实例内重复调用结果一致
            Assert.Equal(all, Localizer.GetAllValues(LocalizationIds.DeepObjArr));
        }

        [Fact]
        public void GetAllValues_OfArray_PreservesElementOrder()
        {
            // 数组顺序必须严格等于下标顺序，这一点不能含糊。
            UseZh();

            Assert.Equal(
                new[] { "v14-arrarr-0-0", "v15-arrarr-0-1", "v16-arrarr-1-0" },
                Localizer.GetAllValues(LocalizationIds.ArrArr));

            Assert.Equal(
                new[] { "v08-strarr-0", "v09-strarr-1", "v10-strarr-2" },
                Localizer.GetAllValues(LocalizationIds.StrArr));
        }

        // ── 与叶子 API 的一致性 ──

        [Fact]
        public void EveryChildIdOfArrayIsReadable()
        {
            UseZh();

            foreach (var arrayId in new[]
                     {
                         LocalizationIds.StrArr, LocalizationIds.OneStrArr,
                         LocalizationIds.ArrArr, LocalizationIds.Cube,
                         LocalizationIds.ObjArr, LocalizationIds.ObjArrArr,
                         LocalizationIds.ArrObj, LocalizationIds.Grid,
                         LocalizationIds.DeepObjArr,
                     })
            {
                foreach (var childId in Localizer.GetChildren(arrayId))
                {
                    Assert.NotEqual(0, Localizer.GetChildCount(childId) + (childId < Catalog.Instance.Keys.Count ? 1 : 0));

                    // 子 id 要么可取文本，要么可继续展开，不能两者皆无
                    var isLeaf = childId < Catalog.Instance.Keys.Count;
                    if (isLeaf)
                    {
                        Assert.False(string.IsNullOrEmpty(Localizer.Get(childId)));
                    }
                    else
                    {
                        Assert.NotEmpty(Localizer.GetChildren(childId));
                    }
                }
            }
        }

        [Fact]
        public void GetValues_AgreesWithGetById()
        {
            UseZh();

            var values = Localizer.GetValues(LocalizationIds.StrArr);
            var children = Localizer.GetChildren(LocalizationIds.StrArr);

            for (var i = 0; i < children.Length; i++)
            {
                Assert.Equal(Localizer.Get(children[i]), values[i]);
            }
        }

        [Fact]
        public void ContainerApis_FollowCultureSwitch()
        {
            Localizer.Install(Catalog.Instance, CultureInfo.GetCultureInfo("zh-CN"));
            var before = Localizer.GetValues(LocalizationIds.StrArr);

            Localizer.SetCulture(CultureInfo.GetCultureInfo("en-US"));
            var after = Localizer.GetValues(LocalizationIds.StrArr);

            Assert.Equal(new[] { "v08-strarr-0", "v09-strarr-1", "v10-strarr-2" }, before);
            Assert.Equal(new[] { "e08-strarr-0", "e09-strarr-1", "e10-strarr-2" }, after);
        }

        [Fact]
        public void FlattenedCount_MatchesLeafCountOfSubtree()
        {
            UseZh();

            // Cube 有 3 个叶子，拍平后正好 3 个
            Assert.Equal(3, Localizer.GetAllValues(LocalizationIds.Cube).Length);

            // Grid 有 4 个叶子（2 行 × 每行 1 个格子 × 2 字段）
            Assert.Equal(4, Localizer.GetAllValues(LocalizationIds.Grid).Length);
        }
    }
}



