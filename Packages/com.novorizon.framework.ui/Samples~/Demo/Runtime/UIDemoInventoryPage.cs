#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Demo
{
    public sealed class UIDemoInventoryPage : UIPage
    {
        UIDemoLauncher? owner;
        UIVirtualList? list;
        Text? details;
        bool dirty;
        int lastWindow = -1;

        protected override void OnCreate()
        {
            CloseGuard = ConfirmCloseAsync;
            RectTransform safe = UIDemoWidgets.SafeContent(transform);
            Text title = UIDemoWidgets.Text("Title", safe, "Inventory: 10,000 logical rows", 30);
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
            viewport.offsetMin = new Vector2(24, 30); viewport.offsetMax = new Vector2(-24, -226);
            var background = viewport.gameObject.AddComponent<Image>(); background.color = UIDemoWidgets.Card;
            viewport.gameObject.AddComponent<RectMask2D>();
            RectTransform content = UIDemoWidgets.Rect("Content", viewport);
            UIDemoWidgets.Stretch(content);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport; scroll.content = content;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            var rowPrefab = new GameObject("Demo Row Template", typeof(RectTransform));
            rowPrefab.SetActive(false); rowPrefab.transform.SetParent(transform, false);
            var rowText = rowPrefab.AddComponent<Text>(); rowText.font = UIDemoWidgets.Font;
            rowText.fontSize = 24; rowText.color = Color.white;
            rowText.alignment = TextAnchor.MiddleLeft; rowText.raycastTarget = false;
            list = viewport.gameObject.AddComponent<UIVirtualList>();
            list.Configure(scroll, rowPrefab, 48, overscan: 1);
        }

        protected override void OnOpen(object? args)
        {
            owner = args as UIDemoLauncher ?? throw new ArgumentException("Demo controller is required.", nameof(args));
            dirty = false; lastWindow = -1;
            list!.SetItemCount(10_000, Bind);
            Track(owner.Root.Bus.SubscribeScoped<UIDemoPulse>(pulse => owner?.Note("Inventory received pulse " + pulse.Value)));
            Update();
        }
        static void Bind(GameObject row, int index)
        {
            Text text = row.GetComponent<Text>();
            text.text = "  Item " + index.ToString("00000") + "   — recycled visible row";
            text.color = index % 2 == 0 ? Color.white : new Color(.55f, .8f, 1, 1);
        }
        void Update()
        {
            if (list == null || details == null || lastWindow == list.FirstVisibleIndex) return;
            lastWindow = list.FirstVisibleIndex;
            details.text = "Rows instantiated: " + list.InstantiatedItemCount + " / 10000; visible: " +
                list.FirstVisibleIndex + ".." + list.LastVisibleIndex + "\nUnsaved changes: " + dirty;
        }
        void MarkDirty() { dirty = true; lastWindow = -1; Update(); owner?.Note("Back now asks for confirmation. Cancel keeps this page open."); }
        void ScrollEnd() { list?.ScrollTo(9999); }
        void Back() { if (owner != null) owner.Run(owner.BackAsync); }
        async Task<bool> ConfirmCloseAsync(UICloseReason reason, CancellationToken token)
        {
            if (!dirty) return true;
            if (owner == null) return false;
            UIDialogResult<bool> result = await owner.AskAsync("Discard the inventory's unsaved demo changes?", token);
            return result.HasValue && result.Value;
        }
        protected override void OnShow() { owner?.Note("Inventory shown; scroll recycles a bounded visible window."); }
        protected override void OnHide() { owner?.Note("Inventory hidden without closing its open scope."); }
        protected override void OnClose() { list?.Clear(); owner = null; }
    }
}
