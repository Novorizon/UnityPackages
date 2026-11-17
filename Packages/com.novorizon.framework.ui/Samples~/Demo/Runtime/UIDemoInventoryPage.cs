#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Demo
{
    /// <summary>提供界面示例物品清单页面的实现。</summary>
    public sealed class UIDemoInventoryPage : UIPage
    {
        UIDemoLauncher? owner;
        UIVirtualList? list;
        TMP_Text? details;
        bool dirty;
        int lastWindow = -1;
        /// <summary>响应 Create。</summary>
        protected override void OnCreate()
        {
            CloseGuard = ConfirmCloseAsync;
            RectTransform safe = UIDemoWidgets.SafeContent(transform);
            TMP_Text title = UIDemoWidgets.Text("Title", safe, "Inventory: 10,000 logical rows", 30);
            UIDemoWidgets.Top((RectTransform)title.transform, 20, 48);
            details = UIDemoWidgets.Text("Details", safe, string.Empty, 20);
            UIDemoWidgets.Top((RectTransform)details.transform, 76, 64);
            Button back = UIDemoWidgets.Button("Back", safe, "Back", Back);
            UIDemoWidgets.Top((RectTransform)back.transform, 150, 56, 24, 12, 0, .33f);
            Button change = UIDemoWidgets.Button("Dirty", safe, "Mark unsaved", MarkDirty);
            UIDemoWidgets.Top((RectTransform)change.transform, 150, 56, 12, 12, .33f, .66f);
            Button end = UIDemoWidgets.Button("ScrollEnd", safe, "Scroll to end", ScrollEnd);
            UIDemoWidgets.Top((RectTransform)end.transform, 150, 56, 12, 24, .66f, 1);
            RectTransform viewport = UIDemoWidgets.Rect("Viewport", safe);
            UIDemoWidgets.Stretch(viewport);
            viewport.offsetMin = new Vector2(24, 30);
            viewport.offsetMax = new Vector2(-24, -226);
            var background = viewport.gameObject.AddComponent<Image>();
            background.color = UIDemoWidgets.Card;
            viewport.gameObject.AddComponent<RectMask2D>();
            RectTransform content = UIDemoWidgets.Rect("Content", viewport);
            UIDemoWidgets.Stretch(content);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            var rowPrefab = new GameObject("Demo Row Template", typeof(RectTransform));
            rowPrefab.SetActive(false);
            rowPrefab.transform.SetParent(transform, false);
            var rowText = rowPrefab.AddComponent<TextMeshProUGUI>();
            rowText.font = UIDemoWidgets.Font;
            rowText.fontSize = 24;
            rowText.color = Color.white;
            rowText.alignment = TextAlignmentOptions.Left;
            rowText.enableWordWrapping = false;
            rowText.overflowMode = TextOverflowModes.Ellipsis;
            rowText.raycastTarget = false;
            list = viewport.gameObject.AddComponent<UIVirtualList>();
            list.Configure(scroll, rowPrefab, 48, overscan: 1);
        }

        /// <summary>响应打开。</summary>
        protected override void OnOpen(object? args)
        {
            owner = args as UIDemoLauncher ?? throw new ArgumentException("Demo controller is required.", nameof(args));
            dirty = false;
            lastWindow = -1;
            list!.SetItemCount(10_000, Bind);
            Track(owner.Root.Bus.SubscribeScoped<UIDemoPulse>(pulse => owner?.Note("Inventory received pulse " + pulse.Value)));
            Update();
        }

        /// <summary>绑定界面示例物品清单页面。</summary>
        static void Bind(GameObject row, int index)
        {
            TMP_Text text = row.GetComponent<TMP_Text>();
            text.text = "  Item " + index.ToString("00000") + "   — recycled visible row";
            text.color = index % 2 == 0 ? Color.white : new Color(.55f, .8f, 1, 1);
        }

        /// <summary>推进该对象的一次更新。</summary>
        void Update()
        {
            if (list == null || details == null || lastWindow == list.FirstVisibleIndex)
            {
                return;
            }

            lastWindow = list.FirstVisibleIndex;
            details.text = "Rows instantiated: " + list.InstantiatedItemCount + " / 10000; visible: " + list.FirstVisibleIndex + ".." + list.LastVisibleIndex + "\nUnsaved changes: " + dirty;
        }

        /// <summary>标记已修改。</summary>
        void MarkDirty()
        {
            dirty = true;
            lastWindow = -1;
            Update();
            owner?.Note("Back now asks for confirmation. Cancel keeps this page open.");
        }

        /// <summary>调用 .ScrollTo 完成滚动 End。</summary>
        void ScrollEnd()
        {
            list?.ScrollTo(9999);
        }

        /// <summary>调用 owner.Run 完成返回。</summary>
        void Back()
        {
            if (owner != null)
            {
                owner.Run(owner.BackAsync);
            }
        }

        /// <summary>异步确认 Close。</summary>
        async Task<bool> ConfirmCloseAsync(UICloseReason reason, CancellationToken token)
        {
            if (!dirty)
            {
                return true;
            }

            if (owner == null)
            {
                return false;
            }

            UIDialogResult<bool> result = await owner.AskAsync("Discard the inventory's unsaved demo changes?", token);
            return result.HasValue && result.Value;
        }

        /// <summary>响应 Show。</summary>
        protected override void OnShow()
        {
            owner?.Note("Inventory shown; scroll recycles a bounded visible window.");
        }

        /// <summary>响应 Hide。</summary>
        protected override void OnHide()
        {
            owner?.Note("Inventory hidden without closing its open scope.");
        }

        /// <summary>响应 Close。</summary>
        protected override void OnClose()
        {
            list?.Clear();
            owner = null;
        }
    }
}
