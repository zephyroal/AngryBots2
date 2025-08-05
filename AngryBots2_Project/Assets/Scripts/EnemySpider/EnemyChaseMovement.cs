using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class EnemyChaseMovement : MonoBehaviour {


	[Header("Chase Behaviour")]
	public Transform player;
	public NavMeshAgent agent;
	private bool playerInRange = false;
	public bool chasingPlayer = false; // 改为public以便EnemyPatrol可以访问
	public Transform lineOfSightOrigin;
	private RaycastHit lineOfSightHit;
	
	[Header("References")]
	public EnemyPatrol patrolBehaviour; // 添加对EnemyPatrol的引用

	[Header("Animation")]
	public Animator animator;


	public void PlayerIsNowInRange()
	{
		playerInRange = true;
	}

	public void PlayerIsNowOutOfRange()
	{
		playerInRange = false;

		if(chasingPlayer){
			StopChasingPlayer();
		}
	}

	void Start()
	{
		// 获取EnemyPatrol组件引用
		if (patrolBehaviour == null)
			patrolBehaviour = GetComponent<EnemyPatrol>();
	}

	void Update()
	{
		if(playerInRange && !chasingPlayer){
			
			Vector3 raycastDirectionTowardsPlayer = player.position - lineOfSightOrigin.position;

			Debug.DrawRay(lineOfSightOrigin.position, raycastDirectionTowardsPlayer * 100f, Color.red);

			// 添加LayerMask参数，确保射线能检测到Player所在的Layer 8
			int layerMask = 1 << 8; // Layer 8的掩码
			if(Physics.Raycast(lineOfSightOrigin.position, raycastDirectionTowardsPlayer, out lineOfSightHit, 100f, layerMask))
			{
				//if(lineOfSightHit.collider.CompareTag("Player"))
				{
					BeginChasingPlayer();
				}
			}

		}

		if(chasingPlayer)
		{
			agent.SetDestination(player.position);
		}
	}

	public void BeginChasingPlayer()
	{
		chasingPlayer = true;
		SetNewAnimationState();
		agent.Resume();
		
		// 通知巡逻组件玩家被发现
		if (patrolBehaviour != null)
			patrolBehaviour.PlayerDetected();
	}

	public void StopChasingPlayer()
	{
		chasingPlayer = false;
		SetNewAnimationState();
		
		// 不再立即停止，而是通知巡逻组件返回巡逻
		if (patrolBehaviour != null)
			patrolBehaviour.ReturnToPatrol();
		else
			agent.Stop(); // 如果没有巡逻组件，则停止移动
	}

	void SetNewAnimationState()
	{
		if(animator)
		{
			animator.SetBool("Chase Player", chasingPlayer);
		}

	}
	
}
