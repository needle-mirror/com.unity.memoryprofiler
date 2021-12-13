using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.MemoryProfiler.Editor.UI.Treemap
{
    internal class Utility
    {
        public static Rect[] GetTreemapRects(float[] values, Rect targetRect)
        {
            if (values.Length == 0)
                throw new ArgumentException("You need to at least pass in one valid value", "values");

            Rect[] result = new Rect[values.Length];

            float totalInputArea = 0f;
            for (int i = 0; i < values.Length; i++)
                totalInputArea += values[i];
            if (totalInputArea <= 0f)
                throw new ArgumentException("The provided values add up to a non-positive total of " + totalInputArea);

            float totalOutputArea = targetRect.width * targetRect.height;
            bool vertical = targetRect.width > targetRect.height;

            var unfinishedRects = new List<Rect>();

            for (int index = 0; index < values.Length; index++)
            {
                bool lastItem = index == values.Length - 1;

                float currentInputValue = values[index];

                if (currentInputValue < 0f)
                    throw new ArgumentException("only positive float values are supported. found: " + currentInputValue);
                if (currentInputValue == 0f)
                {
#if DEBUG_VALIDATION
                    Debug.LogError("Found a zero sized Tree Map Group Item");
#endif
                }

                float currentOutputArea = currentInputValue * totalOutputArea / totalInputArea;
                unfinishedRects = AddRect(unfinishedRects, currentOutputArea, targetRect, vertical);
                float currentAspect = GetAverageAspect(unfinishedRects);

                float nextInputValue = lastItem ? 0f : values[index + 1];
                float nextOutputArea = nextInputValue * totalOutputArea / totalInputArea;
                float nextAspect = GetNextAspect(unfinishedRects, nextOutputArea, targetRect, vertical);

                if (Mathf.Abs(1f - currentAspect) < Mathf.Abs(1f - nextAspect) || lastItem)
                {
                    int resultIndex = index - unfinishedRects.Count + 1;
                    for (int rectIndex = 0; rectIndex < unfinishedRects.Count; rectIndex++)
                    {
                        var rect = unfinishedRects[rectIndex];
                        if (float.IsNaN(rect.x) || float.IsNaN(rect.y))
                        {
                            // Eventually, rects that get too small need to be culled out earlier,
                            // but for the sake of error reduction, replace invalidly small rects with zero rects.
                            // Also, once we hit this size, nothing bigger is going to come after it so, lets speed this up
                            for (int i = resultIndex; i < result.Length; i++)
                            {
                                result[i] = Rect.zero;
                            }
                            return result;
                        }
                        else
                            result[resultIndex++] = rect;
                    }

                    targetRect = GetNewTarget(unfinishedRects, targetRect, vertical);
                    vertical = !vertical;
                    unfinishedRects.Clear();
                }
            }

            return result;
        }

        private static List<Rect> AddRect(List<Rect> existing, float area, Rect space, bool vertical)
        {
            List<Rect> result = new List<Rect>();
            if (vertical)
            {
                if (existing.Count == 0)
                {
                    result.Add(new Rect(space.xMin, space.yMin, space.height > 0 ? area / space.height : 0, space.height));
                }
                else
                {
                    float totalSize = GetArea(existing) + area;
                    float width = space.height > 0 ? totalSize / space.height : 0;
                    float yPosition = space.yMin;
                    foreach (Rect old in existing)
                    {
                        float itemArea = GetArea(old);
                        result.Add(new Rect(old.xMin, yPosition, width, width > 0 ? itemArea / width : 0));
                        yPosition += itemArea / width;
                    }
                    result.Add(new Rect(space.xMin, yPosition, width, width > 0 ? area / width : 0));
                }
            }
            else
            {
                if (existing.Count == 0)
                {
                    result.Add(new Rect(space.xMin, space.yMin, space.width, space.width > 0 ? area / space.width : 0));
                }
                else
                {
                    float totalSize = GetArea(existing) + area;
                    float height = space.width > 0 ? totalSize / space.width : 0;
                    float xPosition = space.xMin;
                    foreach (Rect old in existing)
                    {
                        float itemArea = GetArea(old);
                        result.Add(new Rect(xPosition, old.yMin, height > 0 ? itemArea / height : 0, height));
                        xPosition += itemArea / height;
                    }
                    result.Add(new Rect(xPosition, space.yMin, height > 0 ? area / height : 0, height));
                }
            }
            return result;
        }

        private static Rect GetNewTarget(List<Rect> unfinished, Rect oldTarget, bool vertical)
        {
            if (vertical)
            {
                return new Rect(oldTarget.xMin + unfinished[0].width, oldTarget.yMin, oldTarget.width - unfinished[0].width, oldTarget.height);
            }
            else
            {
                return new Rect(oldTarget.xMin, oldTarget.yMin + unfinished[0].height, oldTarget.width, oldTarget.height - unfinished[0].height);
            }
        }

        private static float GetNextAspect(List<Rect> existing, float area, Rect space, bool vertical)
        {
            List<Rect> newExisting = AddRect(existing, area, space, vertical);
            return newExisting[newExisting.Count - 1].height / newExisting[newExisting.Count - 1].width;
        }

        private static float GetAverageAspect(List<Rect> rects)
        {
            float aspect = 0f;
            var count = rects.Count;
            foreach (Rect r in rects)
            {
                if (r.height > 0f && r.width > 0f)
                {
                    aspect += r.height / r.width;
                }
                else
                {
                    // ignore rects with no surface area
                    --count;
                }
            }
            // avoid NaN if all rects so far have no surface area
            return aspect / Math.Max(1, count);
        }

        private static float GetArea(Rect rect)
        {
            return rect.width * rect.height;
        }

        private static float GetArea(List<Rect> rects)
        {
            float area = 0;
            foreach (Rect r in rects)
            {
                area += GetArea(r);
            }
            return area;
        }

        public static Color GetColorForName(string name)
        {
            int r = 0, g = 0, b = 0;

            for (int i = 0; i < name.Length; i++)
            {
                if (i % 3 == 0)
                {
                    r += (int)name[i];
                }
                else if (i % 3 == 1)
                {
                    g += (int)name[i];
                }
                else
                {
                    b += (int)name[i];
                }
            }

            r %= 128;
            g %= 128;
            b %= 128;

            return new Color32((byte)(r + 96), (byte)(g + 96), (byte)(b + 96), 255);
        }

        public static bool IsInside(Rect lhs, Rect rhs)
        {
            return lhs.xMax > rhs.xMin && lhs.xMin < rhs.xMax && lhs.yMax > rhs.yMin && lhs.yMin < rhs.yMax;
        }
    }
}
