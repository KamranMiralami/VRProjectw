using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using Valve.VR;
using Valve.VR.InteractionSystem;

public class PlayerNetwork : NetworkBehaviour
{
    [SerializeField] float walkSpeed = 10f;
    [SerializeField] Vector2 moveInput;
    [SerializeField] Animator m_Animator;
    public static System.Action<PlayerNetwork> onLocalPlayerSpawned;
    public Transform HandPos;
    public bool HandFull => grabbed != null;
    public ParentHandler grabbed;


    public SteamVR_Action_Vector2 move;
    public SteamVR_Action_Vector2 rotate;
    public SteamVR_Action_Boolean grabTrigger;
    public SteamVR_Action_Boolean disassembleTrigger;
    public SteamVR_Action_Single moveOffsetUp;
    public SteamVR_Action_Single moveOffsetDown;
    public Hand RHand;
    public Hand LHand;
    public Transform player;
    public Transform VR;
    public Transform HeadPos;
    public Transform VRCamera;
    public GameObject Shape;
    bool mBool=false;
    public override void OnNetworkSpawn()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        if (IsLocalPlayer)
        {
            onLocalPlayerSpawned?.Invoke(this);
            if (!InputManager.Instance.IsVR)
            {
                VR.gameObject.SetActive(false);
            }
            else
            {
                move.AddOnChangeListener(OnMoving, RHand.handType);
                grabTrigger.AddOnChangeListener(OnGrabTrigger, LHand.handType);
                disassembleTrigger.AddOnChangeListener(OnDisTrigger, LHand.handType);
                moveOffsetUp.AddOnAxisListener(OnOffsetUp, RHand.handType);
                moveOffsetDown.AddOnAxisListener(OnOffsetDown, RHand.handType);
                MovingVR();
            }
        }
        else
        {
            VR.gameObject.SetActive(false);
        }
        gameObject.name = OwnerClientId.ToString();
        Debug.Log("adding listener");
    }

    private void OnOffsetDown(SteamVR_Action_Single fromAction, SteamVR_Input_Sources fromSource, float newAxis, float newDelta)
    {
        ChangeGrabOffsetServerRpc(new Vector3(0, -2f * Time.deltaTime, 0));
    }

    private void OnOffsetUp(SteamVR_Action_Single fromAction, SteamVR_Input_Sources fromSource, float newAxis, float newDelta)
    {
        ChangeGrabOffsetServerRpc(new Vector3(0, 2f * Time.deltaTime, 0));
    }

    private void OnDisTrigger(SteamVR_Action_Boolean fromAction, SteamVR_Input_Sources fromSource, bool newState)
    {
        if (newState)
        {
            Debug.Log("dis");
            DettachObjectServerRpc(Camera.main.transform.position, Camera.main.transform.forward);
        }
    }

    private void OnGrabTrigger(SteamVR_Action_Boolean fromAction, SteamVR_Input_Sources fromSource, bool newState)
    {
        if(newState)
        {
            Debug.Log("grab");
            AttachObjectServerRpc(Camera.main.transform.position, Camera.main.transform.forward);
        }
    }
    bool isMoving = false;
    private void OnMoving(SteamVR_Action_Vector2 fromAction, SteamVR_Input_Sources fromSource, Vector2 axis, Vector2 delta)
    {
        Vector3 headForward = VRCamera.forward;
        Vector3 movementDirection = (headForward * axis.y + VRCamera.right * axis.x).normalized;
        movementDirection.y = 0f;
        player.transform.position += movementDirection * Time.deltaTime*walkSpeed;
        isMoving = true;
    }

    private void Update()
    {
        if (!IsOwner || !IsSpawned) return;
        if (HandFull && InputManager.Instance.IsVR)
        {
            ChangeGrabRotationWithHandServerRpc();
        }
        Run();
    }
    async void MovingVR()
    {
        while (true)
        {
            if (isMoving)
            {
                AnimationChangeServerRpc(true);
                isMoving = false;
            }
            else
            {
                AnimationChangeServerRpc(false);
            }
            await Task.Delay(100);
        }
    }
    void Run()
    {
        if (InputManager.Instance.IsVR)
            return;
        if (moveInput.x == 0 && moveInput.y == 0){
            AnimationChangeServerRpc(false);
            return;
        }
        transform.position += Time.deltaTime * walkSpeed * (transform.forward * moveInput.y + transform.right * moveInput.x);
        AnimationChangeServerRpc(true);

    }
    public void OnMove(InputValue val)
    {
        moveInput = val.Get<Vector2>();
    }
    [ServerRpc(RequireOwnership = false)]
    public void AttachObjectServerRpc(Vector3 viewPoint,Vector3 forward)
    {
        if (!IsServer) return;
        if (HandFull)
        {
            Debug.Log("Attaching");
            grabbed.Attach();
            grabbed = null;
        }
        else
        {
            Debug.Log("Grabing");
            if (Physics.Raycast
                (viewPoint, forward, out RaycastHit HitInfo, 10f))
            {
                var go = HitInfo.collider.gameObject;
                Debug.Log(go.name);
                if (go.transform.parent != null)
                {
                    if (go.transform.parent.CompareTag("Grab"))
                    {
                        go = go.transform.parent.gameObject;
                    }
                }
                if (go.CompareTag("Grab"))
                {
                    var grabObj = go.transform.parent.GetComponent<ParentHandler>();
                    if (InputManager.Instance.IsVR)
                    {
                        grabObj.FollowObj = LHand.transform;
                    }
                    else
                    {
                        grabObj.FollowObj = HandPos;
                    }
                    grabbed = grabObj;
                }
            }
        }
    }
    [ServerRpc(RequireOwnership = false)]
    public void DettachObjectServerRpc(Vector3 viewPoint, Vector3 forward)
    {
        if (!IsServer) return;
        if (HandFull)
        {
            
        }
        else
        {
            Debug.Log("Detaching");
            if (Physics.Raycast
                (viewPoint, forward, out RaycastHit HitInfo, 5f))
            {
                var go = HitInfo.collider.gameObject;
                Debug.Log(go.name);
                if (go.transform.parent.CompareTag("Grab"))
                {
                    go = go.transform.parent.gameObject;
                }
                if (go.CompareTag("Grab"))
                {
                    var grabObj = go.GetComponent<GrabableObjBehaviour>();
                    var prevParent = go.transform.parent.GetComponent<ParentHandler>();
                    prevParent.grabbed.Remove(grabObj);
                    var grabParent = Instantiate(GameManager.Instance.Prefab);
                    grabParent.GetComponent<NetworkObject>().Spawn();
                    grabParent.MakeParent(grabObj);
                    grabObj.transform.localPosition = Vector3.zero;
                    grabParent.FollowObj = HandPos;
                    grabbed = grabParent;
                }
            }
        }
    }
    [ServerRpc(RequireOwnership = false)]
    public void ChangeGrabOffsetServerRpc(Vector3 v)
    {
        if(grabbed == null) { Debug.Log("cant ");return; }
        grabbed.offset += v;
    }
    [ServerRpc(RequireOwnership = false)]
    public void AnimationChangeServerRpc(bool isMoving)
    {
        if(isMoving)
        {

            m_Animator.SetBool("walk", true);
            m_Animator.SetBool("wait", false);
        }
        else
        {
            m_Animator.SetBool("walk", false);
            m_Animator.SetBool("wait", true);

        }
    }
    [ServerRpc(RequireOwnership = false)]
    public void ChangeGrabRotationServerRpc(Vector3 v)
    {
        if (grabbed == null) { Debug.Log("cant "); return; }
        grabbed.transform.Rotate(v.x*Time.deltaTime, v.y * Time.deltaTime, v.z*Time.deltaTime);
    }
    [ServerRpc(RequireOwnership = false)]
    public void ChangeGrabRotationWithHandServerRpc()
    {
        var eular = LHand.transform.rotation.eulerAngles;
        grabbed.transform.rotation = Quaternion.Euler(eular.x + 60, eular.y - 45, eular.z);
    }
}