﻿/*
MIT License

Copyright (c) 2020 Steffen Vetne

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PrecisionSurfaceEffects
{
    public class SurfaceOutputs : List<SurfaceOutput>
    {
        //This (in theory) pushes the weights downward from anchor so that there is never any "popping". It should bias itself to the remaining highest weights
        //(So long as the weights given were sorted, and there aren't any outputs culled past the maxCount + 1, yet)
        public void Downshift(int maxCount, float minWeight, float mult = 1)
        {
            //This all relies on having the outputs be sorted by decreasing weight
            for (int i = 0; i < Count; i++)
            {
                var val = this[i];
                val.volume *= mult;
                this[i] = val;

                if (this[i].volume < minWeight)
                {
                    RemoveAt(i);
                    i--;
                }
            }

            //int downID = maxCount;
            //if (Count > downID)
            if (Count > 0)
            {
                const float eps = 0.0000001f;

                //for (int i = downID; i >= 0; i--)
                //{
                //    if (this[i].normalizedWeight < minWeight)
                //        downID = i;
                //    else
                //        break;
                //}

                float anchor = this[0].volume; // + eps; //1


                float min = minWeight;
                if (Count > maxCount)
                    min = Mathf.Max(min, this[maxCount].volume);
                min -= eps;
                float downMult = anchor / (anchor - min);

                //Clears any extras
                while (Count > maxCount)
                    RemoveAt(Count - 1);


                for (int i = 0; i < Count; i++)
                {
                    var o = this[i];
                    o.volume = (o.volume - anchor) * downMult + anchor;

                    //if(o.volume < 0) //???? is this possible whatsoever?
                    //{
                    //    RemoveAt(i);
                    //    i--;
                    //}
                    //else
                    this[i] = o;
                }
            }
        }

        private static readonly SOSorter soSorter = new SOSorter();
        private class SOSorter : IComparer<SurfaceOutput>
        {
            public int Compare(SurfaceOutput x, SurfaceOutput y)
            {
                return y.volume.CompareTo(x.volume); //Descending
            }
        }

        public void SortDescending()
        {
            Sort(soSorter);
        }

        public SurfaceOutputs(SurfaceOutputs so) : base(so) { }
        public SurfaceOutputs() : base() { }
    }

    public struct SurfaceOutput
    {
        public int surfaceTypeID;
        public float volume; //normalizedWeight
        public float pitch;
    }
}