using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Random = UnityEngine.Random;

namespace Destruct
{
    enum Side : uint
    {
        AllBelow = 0,
        AllAbove = 7
    }

    public class SplitResult
    {
        public List<Vector3> vertices;
        public List<int> triangles;

        public SplitResult(){}

        public SplitResult(List<Vector3> vertices)
        {
            this.vertices = new(vertices);
            triangles = new();
        }

        public SplitResult(List<Vector3> vertices, List<int> triangles)
        {
            this.vertices = new(vertices);
            this.triangles = new(triangles);
        }
    }

    public class Loop
    {
        public Dictionary<int, int> forwardLinks;
        public Dictionary<int, int> backwardLinks;
        public int first;
        public int last;

        public Loop()
        {
            this.forwardLinks = new();
            this.backwardLinks = new();
            this.first = -1;
            this.last = -1;
        }

        public Loop(Dictionary<int, int> forwardLinks, Dictionary<int, int> backwardLinks, int first, int last)
        {
            this.forwardLinks = forwardLinks;
            this.backwardLinks = backwardLinks;
            this.first = first;
            this.last = last;
        }

        public int Next(int index)
        {
            if (index == last) return first;
            return forwardLinks[index];
        }

        public int Prev(int index)
        {
            if (index == first) return last;
            return backwardLinks[index];
        }

        public void AddLink(int start, int end)
        {
            forwardLinks.Add(start, end);
            backwardLinks.Add(end, start);
            if (!forwardLinks.ContainsKey(end)) last = end;
            if (!backwardLinks.ContainsKey(start)) first = start;
        }

        public void SetLast(int newLast)
        {
            forwardLinks[backwardLinks[last]] = newLast;
            backwardLinks.Add(newLast, backwardLinks[last]);
            backwardLinks.Remove(last);
            last = newLast;
        }

        public void RemoveLink(int index)
        {
            if (index == last)
            {
                forwardLinks.Remove(backwardLinks[index]);
                last = backwardLinks[index];
                backwardLinks.Remove(index);
            }
            else if (index == first)
            {
                backwardLinks.Remove(forwardLinks[index]);
                first = forwardLinks[index];
                forwardLinks.Remove(index);
            }
            else
            {
                forwardLinks[backwardLinks[index]] = forwardLinks[index];
                backwardLinks[forwardLinks[index]] = backwardLinks[index];
                forwardLinks.Remove(index);
                backwardLinks.Remove(index);
            }
        }

        public List<int> ToList()
        {
            List<int> result = new(forwardLinks.Count + 1);
            int index = first;
            while (index != last)
            {
                result.Add(index);
                index = Next(index);
            }
            result.Add(index);
            return result;
        }

        public void Clear()
        {
            forwardLinks.Clear();
            backwardLinks.Clear();
            first = -1;
            last = -1;
        }
    }

    public static class Functions
    {
        public static void Destruct(IDestructible script, int granularity){
            script.PreDestruct();

            List<Vector3> v = new();
            List<int> t = new();

            MeshFilter mFilter = script.GetMeshFilter();
            mFilter.mesh.GetVertices(v);
            mFilter.mesh.GetTriangles(t, 0);
            Vector3 s = script.GetTransform().localScale;

            for(int i = 0; i < v.Count; i++){
                v[i] = new Vector3(v[i].x * s.x, v[i].y * s.y, v[i].z * s.z);
            }
            
            List<SplitResult> result = Fracture(v, t, mFilter.mesh.bounds, granularity);
            
            RemoveLooseVertices(ref result);

            script.PostDestruct(result);
        }

        public static List<SplitResult> Fracture(List<Vector3> vertices, List<int> triangles, Bounds bounds, int granularity)
        {
            List<SplitResult> result = new();
            result.Add(new SplitResult(vertices, triangles));

            List<Plane> planes = new();
            for(int i = 0; i < granularity; i++){
                planes.Add(
                    new Plane(Random.onUnitSphere,
                    new Vector3(
                        UnityEngine.Random.Range(bounds.min.x, bounds.max.x),
                        UnityEngine.Random.Range(bounds.min.y, bounds.max.y),
                        UnityEngine.Random.Range(bounds.min.z, bounds.max.z)
                    )));
            }

            foreach(Plane pl in planes){
                List<SplitResult> newResult = new();
                foreach(SplitResult r in result){
                    var (up, down) = SplitMeshAlongPlane(r.vertices, r.triangles, pl);
                    if(up.triangles.Count > 0) newResult.Add(up);
                    if(down.triangles.Count > 0) newResult.Add(down);
                }
                result = newResult;
            }

            return result;
        }

        public static (SplitResult, SplitResult) SplitMeshAlongPlane(List<Vector3> vertices, List<int> triangles, Plane plane, bool fill = true)
        // 1st SplitResult is above plane
        {
            (SplitResult, SplitResult) result = (new(vertices), new(vertices));
            (Dictionary<int, int>, Dictionary<int, int>) adjacentVertices = (new(vertices.Count), new(vertices.Count));
            Dictionary<(int, int), (int, int)> splitEdges = new(new EdgeComparer());
            BitArray vSides = new BitArray(vertices.Count);
            for (int i = 0; i < vertices.Count; i++)
            {
                vSides[i] = plane.GetSide(vertices[i]);
            }
            List<(int, int)> newPoints = new();

            for (int i = 0; i < triangles.Count; i += 3)
            {
                Vector3[] vPos = { vertices[triangles[i]], vertices[triangles[i + 1]], vertices[triangles[i + 2]] };

                /*
                 * 000 = 0  All vertices below plane
                 * 
                 * 001 = 1
                 * 010 = 2
                 * 011 = 3
                 * 
                 * 100 = 4
                 * 101 = 5
                 * 110 = 6
                 * 
                 * 111 = 7  All vertices above plane
                 * 
                 * I am really not sure if this is necessary, but better than an array and a bunch of if statements
                 */
                uint vSide = 0;
                vSide |= Convert.ToUInt32(vSides[triangles[i]]);
                vSide |= Convert.ToUInt32(vSides[triangles[i + 1]]) << 1;
                vSide |= Convert.ToUInt32(vSides[triangles[i + 2]]) << 2;

                switch ((Side)vSide)
                {
                    case Side.AllBelow:
                        result.Item2.triangles.Add(triangles[i]);
                        result.Item2.triangles.Add(triangles[i + 1]);
                        result.Item2.triangles.Add(triangles[i + 2]);
                        break;

                    case Side.AllAbove:
                        result.Item1.triangles.Add(triangles[i]);
                        result.Item1.triangles.Add(triangles[i + 1]);
                        result.Item1.triangles.Add(triangles[i + 2]);
                        break;

                    default:
                        /*
                        *          Lone(0)
                        *          /  \         This side can be above or below the plane
                        *         0    2        loneIsAbove tell us where
                        *    ----------------
                        *       1        3
                        *      /          \
                        * Previous(2)------Next(1)
                        *   
                        *   SplitEdges go:
                        *   Vertice A
                        *   Vertice B
                        *   New Vertice to A
                        *   New Vertice to B
                        */

                        // Gives position of the lone vertice
                        int loneV = (int)vSide - 1;
                        if (loneV > 2) loneV = 5 - loneV;

                        //              Lone                    Next                           Previous
                        int[] t = { triangles[i + loneV], triangles[i + (loneV + 1) % 3], triangles[i + (loneV + 2) % 3] };
                        int[] newV = { -1, -1, -1, -1 };

                        bool loneIsAbove = Convert.ToBoolean(vSide & (0x001 << loneV));

                        (int, int) split;
                        if (!splitEdges.TryGetValue((t[0], t[2]), out split))
                        {
                            result.Item1.vertices.Add(LinePlaneIntersection(vPos[loneV], vPos[(loneV + 2) % 3], plane));
                            newV[0] = result.Item1.vertices.Count - 1;
                            result.Item2.vertices.Add(result.Item1.vertices[^1]);
                            newV[1] = result.Item2.vertices.Count - 1;


                            splitEdges.Add((t[0], t[2]), (newV[0], newV[1]));

                        }
                        else
                        {
                            newV[0] = split.Item1;
                            newV[1] = split.Item2;
                        }

                        if (!splitEdges.TryGetValue((t[0], t[1]), out split))
                        {

                            result.Item1.vertices.Add(LinePlaneIntersection(vPos[loneV], vPos[(loneV + 1) % 3], plane));
                            newV[2] = result.Item1.vertices.Count - 1;
                            result.Item2.vertices.Add(result.Item1.vertices[^1]);
                            newV[3] = result.Item2.vertices.Count - 1;



                            splitEdges.Add((t[0], t[1]), (newV[2], newV[3]));
                        }
                        else
                        {
                            newV[2] = split.Item1;
                            newV[3] = split.Item2;
                        }

                        if (loneIsAbove)
                        {
                            result.Item1.triangles.Add(t[0]);
                            result.Item1.triangles.Add(newV[2]);
                            result.Item1.triangles.Add(newV[0]);

                            result.Item2.triangles.Add(t[1]);
                            result.Item2.triangles.Add(newV[1]);
                            result.Item2.triangles.Add(newV[3]);

                            result.Item2.triangles.Add(t[2]);
                            result.Item2.triangles.Add(newV[1]);
                            result.Item2.triangles.Add(t[1]);

                            adjacentVertices.Item1.Add(newV[0], newV[2]);
                            adjacentVertices.Item2.Add(newV[3], newV[1]);
                        }
                        else
                        {
                            result.Item2.triangles.Add(t[0]);
                            result.Item2.triangles.Add(newV[2]);
                            result.Item2.triangles.Add(newV[0]);

                            result.Item1.triangles.Add(t[1]);
                            result.Item1.triangles.Add(newV[1]);
                            result.Item1.triangles.Add(newV[3]);

                            result.Item1.triangles.Add(t[2]);
                            result.Item1.triangles.Add(newV[1]);
                            result.Item1.triangles.Add(t[1]);

                            adjacentVertices.Item1.Add(newV[3], newV[1]);
                            adjacentVertices.Item2.Add(newV[0], newV[2]);
                        }

                        newPoints.Add((newV[0], newV[1]));
                        newPoints.Add((newV[2], newV[3]));
                        break;
                }

            }

            List<Loop> loopsUp = EdgesToLoops(result.Item1.vertices, adjacentVertices.Item1);

            foreach (Loop loop in loopsUp)
            {
                List<int> filler = EarClippingTriangulation(Vector3ontoPlane(result.Item1.vertices, loop.ToList(), plane), loop);
                foreach (int v in filler)
                {
                    result.Item1.vertices.Add(result.Item1.vertices[v]);
                    result.Item1.triangles.Add(result.Item1.vertices.Count - 1);
                }
                RevertTriangles(ref filler);
                foreach (int v in filler)
                {
                    result.Item2.vertices.Add(result.Item2.vertices[v]);
                    result.Item2.triangles.Add(result.Item2.vertices.Count - 1);
                }
            }

            return result;
        }

        public static List<GameObject> InstantiateObjectsFromSplitResults(List<SplitResult> results, Vector3 position, Quaternion rotation, Material mat, bool withRigidBody = true){
            List<GameObject> newObjects = new(results.Count);
            foreach(SplitResult res in results){
                var obj = CreateGameObjectFromMeshData("Fragment", position, rotation, res.vertices, res.triangles, mat);
                if(withRigidBody){
                    var col = obj.AddComponent<MeshCollider>();
                    col.convex = true;
                    obj.AddComponent<Rigidbody>();
                }
                newObjects.Add(obj);
            }
            return newObjects;
        }

        public static Vector3 LinePlaneIntersection(Vector3 linePointA, Vector3 linePointB, Plane plane)
        {
            Vector3 lineNormal = (linePointB - linePointA).normalized;

            if (linePointA == linePointB)
                return linePointB;

            return linePointA
                - lineNormal
                * (Vector3.Dot(linePointA + plane.normal * plane.distance, plane.normal)
                / Vector3.Dot(lineNormal, plane.normal));
        }

        public static List<Vector2> Vector3ontoPlane(List<Vector3> vertices, List<int> points, Plane plane)
        {
            Quaternion planeRotation = Quaternion.FromToRotation(plane.normal, Vector3.forward);

            List<Vector2> projectedVertices = new(vertices.Count);
            for (int i = 0; i < vertices.Count; i++) projectedVertices.Add(new Vector2(float.NaN, float.NaN));

            foreach (int idx in points)
            {
                Vector3 temp = planeRotation * vertices[idx];
                projectedVertices[idx] = new Vector2(temp.x, temp.y);
            }

            return projectedVertices;
        }

        public static void RevertTriangles(ref List<int> triangles)
        {
            for (int i = 0; i < triangles.Count; i += 3)
            {
                (triangles[i], triangles[i + 1]) = (triangles[i + 1], triangles[i]);
            }
        }

        public static List<Loop> EdgesToLoops(List<Vector3> vertices, Dictionary<int, int> adjacentVertices)
        {
            List<Loop> result = new();

            while (adjacentVertices.Count > 0)
            {
                Loop loop = new();
                var iter = adjacentVertices.GetEnumerator();
                iter.MoveNext();
                loop.AddLink(iter.Current.Key, iter.Current.Value);
                adjacentVertices.Remove(iter.Current.Key);

                while (adjacentVertices.ContainsKey(loop.last) && adjacentVertices[loop.last] != loop.first)
                {
                    int previousLast = loop.last;
                    if (vertices[loop.last] == vertices[adjacentVertices[loop.last]])
                    {
                        loop.SetLast(adjacentVertices[loop.last]);
                    }
                    else
                    {
                        loop.AddLink(loop.last, adjacentVertices[loop.last]);
                    }
                    adjacentVertices.Remove(previousLast);
                }

                if (adjacentVertices.ContainsKey(loop.last)) adjacentVertices.Remove(loop.last);

                result.Add(loop);
            }

            HashSet<int> unevaluatedLoops = new(result.Count);
            for (int i = 0; i < result.Count; i++)
            {
                if (vertices[result[i].first] != vertices[result[i].last])
                {
                    unevaluatedLoops.Add(i);
                }
                else
                {
                    result[i].Clear();
                }
            }

            for (int i = 0; i < result.Count; i++)
            {
                if (!unevaluatedLoops.Contains(i)) continue;
                unevaluatedLoops.Remove(i);

                int matchingLoop = -1;

                foreach (int loop in unevaluatedLoops)
                {
                    if ((vertices[result[loop].first] == vertices[result[i].last])
                        || (vertices[result[loop].last] == vertices[result[i].first]))
                    {
                        matchingLoop = loop;
                        break;
                    }
                }

                while (matchingLoop != -1)
                {

                    result[matchingLoop].forwardLinks.ToList().ForEach(x => result[i].forwardLinks.Add(x.Key, x.Value));
                    result[matchingLoop].backwardLinks.ToList().ForEach(x => result[i].backwardLinks.Add(x.Key, x.Value));

                    if (vertices[result[matchingLoop].last] == vertices[result[i].first])
                    {
                        if (result[matchingLoop].last != result[i].first) result[i].AddLink(result[matchingLoop].last, result[i].first);
                        result[i].first = result[matchingLoop].first;
                    }
                    else
                    {
                        if (result[matchingLoop].first != result[i].last) result[i].AddLink(result[i].last, result[matchingLoop].first);
                        result[i].last = result[matchingLoop].last;
                    }

                    result[matchingLoop].Clear();
                    unevaluatedLoops.Remove(matchingLoop);

                    matchingLoop = -1;

                    foreach (int loop in unevaluatedLoops)
                    {
                        if ((vertices[result[loop].first] == vertices[result[i].last])
                            || (vertices[result[loop].last] == vertices[result[i].first]))
                        {
                            matchingLoop = loop;
                            break;
                        }
                    }
                }
            }

            result.RemoveAll(loop => loop.first == -1);

            foreach (Loop loop in result)
            {
                int index = loop.Next(loop.first);
                do
                {
                    while (vertices[index] == vertices[loop.Prev(index)]) loop.RemoveLink(loop.Prev(index));
                    index = loop.Next(index);
                } while (index != loop.Next(loop.first));
            }

            return result;
        }

        public static List<int> EarClippingTriangulation(List<Vector2> vertices, Loop loop)
        {
            List<int> result = new();
            int index = loop.first;

#if DEBUG
            int notRemovedCounter = 0;
#endif

            while (loop.forwardLinks.Count > 1)
            {
                int next = loop.Next(index);
                int prev = loop.Prev(index);
                Vector2 a = vertices[prev] - vertices[index];
                Vector2 b = vertices[next] - vertices[index];

                float crossZ = a.x * b.y - a.y * b.x;

                if (crossZ > 0)
                {
                    bool isValid = true;
                    int it = loop.Next(next);
                    while (it != prev)
                    {
                        if (PointInTriangle(vertices[it], vertices[next], vertices[index], vertices[prev]))
                        {
                            isValid = false;
                            break;
                        }
                        it = loop.Next(it);
                    }
                    if (isValid)
                    {
                        result.Add(index);
                        result.Add(next);
                        result.Add(prev);
                        loop.RemoveLink(index);
#if DEBUG
                        notRemovedCounter = 0;
#endif
                    }
                }
                else if (crossZ > -1e8)
                {
                    loop.RemoveLink(index);
#if DEBUG
                    notRemovedCounter = 0;
#endif
                }

#if DEBUG
                else
                {
                    notRemovedCounter++;
                }
                if (notRemovedCounter > loop.forwardLinks.Count+1)
                {
                    throw new Exception();
                }
#endif

                index = next;
            }

            return result;
        }

        public static GameObject CreateGameObjectFromMeshData(string name, Vector3 position, Quaternion rotation, List<Vector3> vertices, List<int> triangles, Material mat)
        {

            GameObject obj = new GameObject(name);

            obj.transform.position = position;
            obj.transform.rotation = rotation;

            MeshFilter mFilter = obj.AddComponent<MeshFilter>();
            mFilter.mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            MeshRenderer mRenderer = obj.AddComponent<MeshRenderer>();

            mFilter.mesh.SetVertices(vertices);
            mFilter.mesh.MarkDynamic();
            Material[] materials = mRenderer.materials;
            materials[0] = mat;
            mRenderer.materials = materials;
            mFilter.mesh.SetTriangles(triangles, 0);
            mFilter.mesh.RecalculateNormals();
            mFilter.mesh.RecalculateTangents();
            return obj;
        }

        public static void RemoveLooseVertices(ref List<Vector3> vertices, ref List<int> triangles)
        {
            List<Vector3> newVertices = new(vertices.Count);
            List<int> newVerticePositions = Enumerable.Repeat(0, vertices.Count).ToList();

            HashSet<int> usedVertices = new(triangles);

            for (int i = 0; i < vertices.Count; i++)
            {
                if (usedVertices.Contains(i))
                {
                    newVertices.Add(vertices[i]);
                    newVerticePositions[i] = newVertices.Count - 1;
                }
            }

            for (int i = 0; i < triangles.Count; i++)
            {
                triangles[i] = newVerticePositions[triangles[i]];
            }

            vertices = newVertices;
        }

        public static void RemoveLooseVertices(ref List<SplitResult> results){
            foreach(SplitResult res in results){
                RemoveLooseVertices(ref res.vertices, ref res.triangles);
            }
        }

        public static float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
        {
            return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
        }

        public static bool PointInTriangle(Vector2 pt, Vector2 v1, Vector2 v2, Vector2 v3)
        {
            float d1, d2, d3;
            bool has_neg, has_pos;

            d1 = Sign(pt, v1, v2);
            d2 = Sign(pt, v2, v3);
            d3 = Sign(pt, v3, v1);

            has_neg = (d1 < 0) || (d2 < 0) || (d3 < 0);
            has_pos = (d1 > 0) || (d2 > 0) || (d3 > 0);

            return !(has_neg && has_pos);
        }
}

    public class EdgeComparer : IEqualityComparer<(int, int)>
    {
        public bool Equals((int, int) edgeOne, (int, int) edgeTwo)
        {
            return (edgeOne.Item1 == edgeTwo.Item1 && edgeOne.Item2 == edgeTwo.Item2)
                || (edgeOne.Item1 == edgeTwo.Item2 && edgeOne.Item2 == edgeTwo.Item1);
        }

        public int GetHashCode((int, int) edge)
        {
            Debug.Assert(edge.Item1 >= 0 && edge.Item2 >= 0);
            return (edge.Item1 + edge.Item2).GetHashCode();
        }
    }
}