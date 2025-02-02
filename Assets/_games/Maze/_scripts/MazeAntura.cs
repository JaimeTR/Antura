﻿using Antura.Dog;
using Antura.Audio;
using Antura.Helpers;
using UnityEngine;
using System.Collections;

namespace Antura.Minigames.Maze
{
    public class MazeAntura : MonoBehaviour
    {
        public const float ANTURA_SPEED = 17.5f;
        public bool IsAnturaTime { get; private set; }

        private Vector3 target;

        private float nextAnturaBarkTimer;
        private Vector3 initialPosition;

        void Start()
        {
            target = transform.position;
            initialPosition = transform.position;
        }

        void Update()
        {
            var distance = target - transform.position;
            distance.y = 0;

            if (IsAnturaTime)
            {
                if (nextAnturaBarkTimer <= 0)
                {
                    PrepareNextAnturaBark();
                }
                else
                {
                    nextAnturaBarkTimer -= Time.deltaTime;
                }
            }

            if (distance.sqrMagnitude < 0.1f)
            {
                transform.position = target;
                // reached
                //if (IsAnturaTime)
                //	SetRandomTarget();
                if (IsAnturaTime)
                {
                    //SetAnturaTime (false, initialPosition);
                    GetComponent<AnturaAnimationController>().State = AnturaAnimationStates.sitting;
                    StartCoroutine(waitAndReturn(2.5f));
                }
            }
            else
            {
                distance.Normalize();
                transform.position += distance * Vector3.Dot(distance, transform.forward) * ANTURA_SPEED * Time.deltaTime;
                GameplayHelper.LerpLookAtPlanar(transform, target, Time.deltaTime * 3);
            }
        }

        IEnumerator waitAndReturn(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            SetAnturaTime(false, initialPosition);
        }

        public bool returnedToIdle()
        {
            return IsAnturaTime == false && target == transform.position;
        }

        public void SetAnturaTime(bool _isAnturaTime, Vector3 position)
        {
            if (_isAnturaTime == IsAnturaTime)
            {
                return;
            }

            IsAnturaTime = _isAnturaTime;

            GetComponent<AnturaAnimationController>().SetWalkingSpeed(ANTURA_SPEED);
            GetComponent<AnturaAnimationController>().State = AnturaAnimationStates.walking;

            if (IsAnturaTime)
            {
                target = position;
                PrepareNextAnturaBark();
            }
            else
            {
                target = initialPosition;
            }

            AudioManager.I.PlaySound(Sfx.DogBarking);
        }

        void PrepareNextAnturaBark()
        {
            nextAnturaBarkTimer = Random.Range(1, 3);
        }
    }
}
