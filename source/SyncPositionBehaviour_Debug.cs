/*
MIT License

Copyright (c) 2021 James Frowen

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

using System.Collections.Generic;
using Mirage;
using UnityEngine;

namespace JamesFrowen.PositionSync
{
    [RequireComponent(typeof(SyncPositionBehaviour))]
    public class SyncPositionBehaviour_Debug : NetworkBehaviour
    {
        SyncPositionBehaviour behaviour;
        List<GameObject> markers = new List<GameObject>();
        SyncPositionSystem _system;
        public float maxTime = 5;
        public float MaxScale = 1;

        private void Awake()
        {
            behaviour = GetComponent<SyncPositionBehaviour>();
        }
        private void Update()
        {
            if (!IsClient) return;
            if (_system == null)
                _system = ClientObjectManager.GetComponent<SyncPositionSystem>();

            foreach (GameObject marker in markers)
            {
                marker.SetActive(false);
            }
            IReadOnlyList<SnapshotBuffer<TransformState>.Snapshot> buffer = behaviour.snapshotBuffer.DebugBuffer;
            for (int i = 0; i < buffer.Count; i++)
            {
                SnapshotBuffer<TransformState>.Snapshot snapshot = buffer[i];
                if (markers.Count <= i) markers.Add(CreateMarker());

                markers[i].SetActive(true);
                markers[i].transform.SetPositionAndRotation(snapshot.state.position, snapshot.state.rotation);
                Vector3 pos = snapshot.state.position;
                float hash = pos.x * 501 + pos.z;
                markers[i].GetComponent<Renderer>().material.color = Color.HSVToRGB((hash * 20) % 1, 1, 1);
                float snapshotTime = _system.TimeSync.InterpolationTimeField;

                float absTimeDiff = Mathf.Abs(snapshotTime - (float)snapshot.time);
                float sizeFromDiff = Mathf.Clamp01((maxTime - absTimeDiff) / maxTime);
                float scale = sizeFromDiff * MaxScale;
                markers[i].transform.localScale = Vector3.one * scale;
            }
        }

        private GameObject CreateMarker()
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);

            return marker;
        }
    }
}
