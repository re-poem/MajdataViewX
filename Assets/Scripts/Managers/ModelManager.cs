using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using static MajdataViewX.Base.MajCtx;

namespace MajdataViewX.Managers
{
    public class ModelManager : MonoBehaviour
    {
        [SerializeField]
        private Animator _animator;

        public Vector3 LeftHandPos;
        public Quaternion LeftHandRot;
        public Vector3 RightHandPos;
        public Quaternion RightHandRot;

        private void Awake()
        {
            _modelManager = this;
        }
        private void Start()
        {
            LeftHandPos = Vector3.zero;
            RightHandPos = Vector3.zero;
            LeftHandRot = Quaternion.identity;
            RightHandRot = Quaternion.identity;
        }
        private void OnAnimatorIK(int layerIndex)
        {
            _animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 1);
            _animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 1);
            _animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 1);
            _animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 1);

            _animator.SetIKPosition(AvatarIKGoal.LeftHand, LeftHandPos);
            _animator.SetIKPosition(AvatarIKGoal.RightHand, RightHandPos);
            _animator.SetIKRotation(AvatarIKGoal.LeftHand, LeftHandRot);
            _animator.SetIKRotation(AvatarIKGoal.RightHand, RightHandRot);


            _animator.SetIKPositionWeight(AvatarIKGoal.LeftFoot, 1);
            _animator.SetIKPositionWeight(AvatarIKGoal.RightFoot, 1);
            _animator.SetIKPosition(AvatarIKGoal.LeftFoot, new Vector3(0, -99999, 0));
            _animator.SetIKPosition(AvatarIKGoal.RightFoot, new Vector3(0, -99999, 0));
        }
    }
}
