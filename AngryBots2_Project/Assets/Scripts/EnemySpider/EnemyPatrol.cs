using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class EnemyPatrol : MonoBehaviour 
{
    [Header("Patrol Settings")]
    public float patrolSpeed = 3.5f;
    public float sleepTime = 3f;
    public List<Transform> patrolPoints = new List<Transform>();
    
    [Header("References")]
    public NavMeshAgent agent;
    public EnemyChaseMovement chaseMovement;
    public Animator animator;
    
    private int currentPatrolIndex = 0;
    private bool patrolForward = true;
    private bool isSleeping = false;
    private bool isPatrolling = true;
    private Vector3 originalPosition;
    private Coroutine sleepCoroutine;
    
    void Start()
    {
        if (agent == null)
            agent = GetComponent<NavMeshAgent>();
            
        if (chaseMovement == null)
            chaseMovement = GetComponent<EnemyChaseMovement>();
            
        if (animator == null && chaseMovement != null)
            animator = chaseMovement.animator;
            
        originalPosition = transform.position;
        
        // 如果没有设置巡逻点，创建一个默认的巡逻路径
        if (patrolPoints.Count == 0)
        {
            CreateDefaultPatrolPath();
        }
        
        // 开始巡逻
        StartPatrolling();
    }
    
    void Update()
    {
        // 即使在巡逻状态下，也要允许追逐行为
        if (!isPatrolling || isSleeping)
            return;
            
        // 如果正在追逐玩家，则不执行巡逻逻辑
        if (chaseMovement.chasingPlayer)
            return;
            
        // 检查是否到达当前巡逻点
        if (!agent.pathPending && agent.remainingDistance < 0.5f)
        {
            GoToNextPatrolPoint();
        }
    }
    
    // 创建默认的巡逻路径
    void CreateDefaultPatrolPath()
    {
        GameObject patrolPointsParent = new GameObject("PatrolPoints");
        patrolPointsParent.transform.parent = transform.parent;
        
        // 创建四个巡逻点，形成一个矩形
        for (int i = 0; i < 4; i++)
        {
            GameObject point = new GameObject("PatrolPoint_" + i);
            point.transform.parent = patrolPointsParent.transform;
            
            // 设置巡逻点位置
            Vector3 position = originalPosition;
            float offset = 5f;
            
            switch (i)
            {
                case 0:
                    position += new Vector3(offset, 0, offset);
                    break;
                case 1:
                    position += new Vector3(-offset, 0, offset);
                    break;
                case 2:
                    position += new Vector3(-offset, 0, -offset);
                    break;
                case 3:
                    position += new Vector3(offset, 0, -offset);
                    break;
            }
            
            point.transform.position = position;
            patrolPoints.Add(point.transform);
        }
    }
    
    // 开始巡逻
    public void StartPatrolling()
    {
        if (patrolPoints.Count == 0)
            return;
            
        isPatrolling = true;
        isSleeping = false;
        
        if (sleepCoroutine != null)
            StopCoroutine(sleepCoroutine);
            
        // 设置导航代理的速度
        agent.speed = patrolSpeed;
        agent.isStopped = false;
        
        // 前往第一个巡逻点
        GoToPatrolPoint(currentPatrolIndex);
        
        // 设置动画状态
        if (animator != null)
            animator.SetBool("Chase Player", true);
    }
    
    // 前往指定的巡逻点
    void GoToPatrolPoint(int index)
    {
        if (patrolPoints.Count == 0)
            return;
            
        if (index >= 0 && index < patrolPoints.Count)
        {
            agent.SetDestination(patrolPoints[index].position);
        }
    }
    
    // 前往下一个巡逻点
    void GoToNextPatrolPoint()
    {
        if (patrolPoints.Count == 0)
            return;
            
        // 根据巡逻方向更新索引
        if (patrolForward)
        {
            currentPatrolIndex++;
            if (currentPatrolIndex >= patrolPoints.Count)
            {
                // 到达终点，改变方向
                currentPatrolIndex = patrolPoints.Count - 2;
                patrolForward = false;
                
                // 如果只有一个巡逻点，直接进入休眠
                if (patrolPoints.Count <= 1)
                {
                    EnterSleepMode();
                    return;
                }
            }
        }
        else
        {
            currentPatrolIndex--;
            if (currentPatrolIndex < 0)
            {
                // 完成一次来回巡逻，进入休眠状态
                EnterSleepMode();
                return;
            }
        }
        
        // 前往下一个巡逻点
        GoToPatrolPoint(currentPatrolIndex);
    }
    
    // 进入休眠状态
    void EnterSleepMode()
    {
        isSleeping = true;
        isPatrolling = false;
        agent.isStopped = true;
        
        // 设置动画状态
        if (animator != null)
            animator.SetBool("Chase Player", false);
            
        // 启动休眠计时器
        sleepCoroutine = StartCoroutine(SleepTimer());
    }
    
    // 休眠计时器
    IEnumerator SleepTimer()
    {
        yield return new WaitForSeconds(sleepTime);
        
        // 休眠结束，重新开始巡逻
        currentPatrolIndex = 0;
        patrolForward = true;
        StartPatrolling();
    }
    
    // 当玩家被发现时，停止巡逻并开始追逐
    public void PlayerDetected()
    {
        isPatrolling = false;
        agent.isStopped = true;
        
        if (sleepCoroutine != null)
            StopCoroutine(sleepCoroutine);
            
        // 设置动画状态
        if (animator != null)
            animator.SetBool("Chase Player", true);
    }
    
    // 当停止追逐玩家时，返回原始巡逻点并继续巡逻
    public void ReturnToPatrol()
    {
        // 找到最近的巡逻点
        float minDistance = float.MaxValue;
        int nearestPointIndex = 0;
        
        for (int i = 0; i < patrolPoints.Count; i++)
        {
            float distance = Vector3.Distance(transform.position, patrolPoints[i].position);
            if (distance < minDistance)
            {
                minDistance = distance;
                nearestPointIndex = i;
            }
        }
        
        // 设置当前巡逻点为最近的点
        currentPatrolIndex = nearestPointIndex;
        patrolForward = true;
        
        // 重新开始巡逻
        StartPatrolling();
    }
    
    // 在编辑器中绘制巡逻路径
    void OnDrawGizmos()
    {
        if (patrolPoints.Count == 0)
            return;
            
        // 绘制巡逻路径
        Gizmos.color = Color.green;
        for (int i = 0; i < patrolPoints.Count; i++)
        {
            if (patrolPoints[i] != null)
            {
                Gizmos.DrawSphere(patrolPoints[i].position, 0.5f);
                
                // 绘制连接线
                if (i < patrolPoints.Count - 1 && patrolPoints[i+1] != null)
                {
                    Gizmos.DrawLine(patrolPoints[i].position, patrolPoints[i+1].position);
                }
            }
        }
        
        // 连接最后一个点和第一个点
        if (patrolPoints.Count > 1 && patrolPoints[0] != null && patrolPoints[patrolPoints.Count-1] != null)
        {
            Gizmos.DrawLine(patrolPoints[patrolPoints.Count-1].position, patrolPoints[0].position);
        }
    }
}