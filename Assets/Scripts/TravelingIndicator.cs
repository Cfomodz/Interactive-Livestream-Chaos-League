using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class TravelingIndicator : MonoBehaviour
{
    public TI_Type TI_Type;

    //public TravelingIndicatorIO Origin;
    public Vector3 Origin; 
    public TravelingIndicatorIO Target;

    public long value;

    /// <summary>How many invite-bonus hops up the chain this gold has travelled (0 = earned directly).</summary>
    public int ChainDepth;

}
