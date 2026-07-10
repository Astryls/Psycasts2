#nullable disable
using UnityEngine;
using Verse;

namespace PsycastSynergies
{
    // Flat, auto-hiding scroll views - mirrored from Modern Psycasts UI's FlatScroll (like MXStyle
    // mirrors its UIStyle) so our windows scroll exactly like the rest of the Modern X suite, with
    // no hard dependency on that mod. Native Unity bars are suppressed (wheel scrolling keeps
    // working); a slim flat gray thumb is overlaid only when content overflows. Each scroll view
    // passes a unique id so drags don't cross-talk.
    internal static class FlatScroll
    {
        public const float BarW = 10f;   // width reserved for the bar when shown

        private static bool  _sbDragging;
        private static int   _sbDragId = -1;
        private static float _sbDragOffset;

        public static float ScrollViewWidth(Rect outRect, float contentH) =>
            contentH > outRect.height ? outRect.width - BarW : outRect.width;

        public static void Begin(Rect outRect, ref Vector2 scroll, Rect viewRect)
        {
            Widgets.BeginScrollView(outRect, ref scroll, viewRect, showScrollbars: false);
        }

        public static void End(Rect outRect, ref Vector2 scroll, Rect viewRect, int id)
        {
            Widgets.EndScrollView();

            float contentH = viewRect.height;
            if (contentH <= outRect.height)
            {
                if (_sbDragId == id) { _sbDragging = false; _sbDragId = -1; }
                scroll.y = 0f;
                return;   // fits - no bar
            }

            float trackX    = outRect.xMax - BarW + 3f;
            float maxScroll  = contentH - outRect.height;
            float thumbH     = Mathf.Max(24f, outRect.height * (outRect.height / contentH));
            float tFrac      = maxScroll > 0f ? Mathf.Clamp01(scroll.y / maxScroll) : 0f;
            float thumbY     = outRect.y + tFrac * (outRect.height - thumbH);
            var hitZone = new Rect(trackX - 3f, outRect.y, BarW, outRect.height);
            var thumb   = new Rect(trackX, thumbY, 4f, thumbH);

            var ev = Event.current;
            bool hov = Mouse.IsOver(hitZone);
            bool dragging = _sbDragging && _sbDragId == id;

            if (!dragging && hov && ev.type == EventType.MouseDown && ev.button == 0 && !_sbDragging)
            {
                if (thumb.Contains(ev.mousePosition))
                {
                    _sbDragOffset = ev.mousePosition.y - thumbY;
                }
                else
                {
                    float ny = Mathf.Clamp(ev.mousePosition.y - thumbH * 0.5f, outRect.y, outRect.yMax - thumbH);
                    scroll.y = (ny - outRect.y) / Mathf.Max(1f, outRect.height - thumbH) * maxScroll;
                    _sbDragOffset = thumbH * 0.5f;
                }
                _sbDragging = true; _sbDragId = id; dragging = true;
                ev.Use();
            }

            if (dragging)
            {
                if (ev.type == EventType.MouseDrag)
                {
                    float ny = Mathf.Clamp(ev.mousePosition.y - _sbDragOffset, outRect.y, outRect.yMax - thumbH);
                    scroll.y = (ny - outRect.y) / Mathf.Max(1f, outRect.height - thumbH) * maxScroll;
                    ev.Use();
                }
                else if (ev.type == EventType.MouseUp)
                {
                    _sbDragging = false; _sbDragId = -1;
                    ev.Use();
                }
                tFrac  = maxScroll > 0f ? Mathf.Clamp01(scroll.y / maxScroll) : 0f;
                thumbY = outRect.y + tFrac * (outRect.height - thumbH);
                thumb  = new Rect(trackX, thumbY, 4f, thumbH);
            }

            Widgets.DrawBoxSolid(new Rect(trackX, outRect.y, 4f, outRect.height),
                new Color(1f, 1f, 1f, 0.05f));
            float a = dragging ? 0.45f : hov ? 0.32f : 0.16f;
            Widgets.DrawBoxSolid(thumb, new Color(1f, 1f, 1f, a));
        }
    }
}
