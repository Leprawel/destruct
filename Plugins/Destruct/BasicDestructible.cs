using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Destruct;

[RequireComponent(typeof(MeshFilter))]
public class BasicDestructible : MonoBehaviour, IDestructible
{
    MeshFilter mFilter;
    
    void Start()
    {
        mFilter = GetComponent<MeshFilter>();
    }

    MeshFilter IDestructible.GetMeshFilter(){
        return mFilter;
    }

    Transform IDestructible.GetTransform(){
        return transform;
    }

    void IDestructible.PreDestruct(){
        return;
    }
    
    void IDestructible.PostDestruct( List<SplitResult> destructionResults ){
        var objects = Functions.InstantiateObjectsFromSplitResults(destructionResults, transform.position, transform.rotation, GetComponent<MeshRenderer>().material);

        foreach(GameObject obj in objects){
            obj.AddComponent<BasicDestructible>();
            var rb = obj.GetComponent<Rigidbody>();
            rb.AddForce(Random.onUnitSphere * 10f, ForceMode.VelocityChange);
        }
        Destroy(gameObject);
    }
}
