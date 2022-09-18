using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Destruct;

public interface IDestructible
{
    void PreDestruct();

    void PostDestruct( List<SplitResult> destructionResults );

    MeshFilter GetMeshFilter();

    Transform GetTransform();
}
