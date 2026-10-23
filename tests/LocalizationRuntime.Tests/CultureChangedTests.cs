using System;
using System.Collections.Generic;
using System.Globalization;
using LocalizationRuntime.Tests.Localization;
using Xunit;

namespace LocalizationRuntime.Tests
{
    /// <summary>
    /// 语言切换通知。
    ///
    /// 用途是让订阅者知道"文本已经换了，缓存过的属性该重读"，
    /// 因此最关键的一条是：事件触发时取值必须已经是新语言。
    /// </summary>
    public sealed class CultureChangedTests
    {
        private static Localizer Create(string culture)
        {
            return new Localizer(Catalog.Instance, CultureInfo.GetCultureInfo(culture));
        }

        [Fact]
        public void ChangingCulture_RaisesTheEvent()
        {
            var l = Create("zh-CN");

            CultureChangedEventArgs? received = null;
            l.CultureChanged += (sender, e) => received = e;

            Assert.True(l.SetCulture(CultureInfo.GetCultureInfo("en-US")));

            Assert.NotNull(received);
            Assert.Equal("zh-CN", received!.OldCulture.Name);
            Assert.Equal("en-US", received.NewCulture.Name);
            Assert.Equal(l.Revision, received.Revision);
        }

        /// <summary>
        /// 关键语义：处理函数里取值必须拿到新语言，
        /// 否则"收到通知就刷新属性"会刷出一批旧值。
        /// </summary>
        [Fact]
        public void HandlerSeesTheNewLanguageValues()
        {
            var l = Create("zh-CN");
            Assert.Equal("v01-scalar", l.Get(LocalizationIds.Scalar));

            string? seenInsideHandler = null;
            l.CultureChanged += (sender, e) => seenInsideHandler = l.Get(LocalizationIds.Scalar);

            l.SetCulture(CultureInfo.GetCultureInfo("en-US"));

            Assert.Equal("e01-scalar", seenInsideHandler);
        }

        [Fact]
        public void SettingTheSameCulture_RaisesNothing()
        {
            var l = Create("zh-CN");

            var calls = 0;
            l.CultureChanged += (sender, e) => calls++;

            Assert.False(l.SetCulture(CultureInfo.GetCultureInfo("zh-CN")));
            Assert.Equal(0, calls);
        }

        [Fact]
        public void Unsubscribing_StopsNotifications()
        {
            var l = Create("zh-CN");

            var calls = 0;
            EventHandler<CultureChangedEventArgs> handler = (sender, e) => calls++;

            l.CultureChanged += handler;
            l.SetCulture(CultureInfo.GetCultureInfo("en-US"));
            Assert.Equal(1, calls);

            l.CultureChanged -= handler;
            l.SetCulture(CultureInfo.GetCultureInfo("zh-CN"));
            Assert.Equal(1, calls);
        }

        [Fact]
        public void EverySwitch_RaisesOnceInOrder()
        {
            var l = Create("zh-CN");

            var seen = new List<string>();
            l.CultureChanged += (sender, e) => seen.Add(e.ToString());

            // 测试资源只有 zh-CN / en-US 两种语言
            l.SetCulture(CultureInfo.GetCultureInfo("en-US"));
            l.SetCulture(CultureInfo.GetCultureInfo("zh-CN"));
            l.SetCulture(CultureInfo.GetCultureInfo("en-US"));

            Assert.Equal(
                new[] { "zh-CN → en-US", "en-US → zh-CN", "zh-CN → en-US" },
                seen);
        }

        /// <summary>
        /// 不受支持的语言会落到基语言，事件报的应该是实际生效的那个。
        /// </summary>
        [Fact]
        public void UnsupportedCulture_ReportsTheEffectiveOne()
        {
            var l = Create("en-US");

            CultureChangedEventArgs? received = null;
            l.CultureChanged += (sender, e) => received = e;

            l.SetCulture(CultureInfo.GetCultureInfo("de-DE"));

            Assert.NotNull(received);
            Assert.Equal("zh-CN", received!.NewCulture.Name);
            Assert.Equal("zh-CN", l.CurrentCulture.Name);
        }

        /// <summary>
        /// 事件在锁外触发，因此处理函数里可以安全地再切一次语言。
        /// </summary>
        [Fact]
        public void HandlerMaySwitchCultureAgain()
        {
            var l = Create("zh-CN");

            var depth = 0;
            l.CultureChanged += (sender, e) =>
            {
                if (++depth == 1)
                {
                    // 在事件处理里再切一次，验证不会因为持锁而死掉
                    l.SetCulture(CultureInfo.GetCultureInfo("zh-CN"));
                }
            };

            l.SetCulture(CultureInfo.GetCultureInfo("en-US"));

            Assert.Equal(2, depth);
            Assert.Equal("zh-CN", l.CurrentCulture.Name);
        }

        [Fact]
        public void EventArgs_RejectsNulls()
        {
            Assert.Throws<ArgumentNullException>(
                () => new CultureChangedEventArgs(null!, CultureInfo.InvariantCulture, 0));

            Assert.Throws<ArgumentNullException>(
                () => new CultureChangedEventArgs(CultureInfo.InvariantCulture, null!, 0));
        }
    }
}