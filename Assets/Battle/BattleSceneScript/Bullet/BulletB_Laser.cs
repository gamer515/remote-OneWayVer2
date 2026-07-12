using UnityEngine;
using System.Collections;

public class BulletB_Laser : MonoBehaviour
{
    public GameObject laserBeamVisual; // 자식으로 있는 하얀색 사각형 스프라이트 연결

    IEnumerator Start()
    {
        laserBeamVisual.SetActive(false);
        // 생성 후 0.2초 대기
        yield return new WaitForSeconds(0.7f);

        // 레이저 발사 (하얀색 칠하기 & 콜라이더 켜기)
        laserBeamVisual.SetActive(true);

        // 0.5초 뒤 레이저 포 전체 소멸
        yield return new WaitForSeconds(0.5f);
        Destroy(gameObject);
    }
}