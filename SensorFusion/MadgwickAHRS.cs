using System;
namespace BLE_Interface.SensorFusion
{    public class MadgwickAHRS
    {
        public float SamplePeriod { get; set; }
        public float Beta { get; set; }

        public float Q0 { get; private set; } = 1.0f;
        public float Q1 { get; private set; } = 0.0f;
        public float Q2 { get; private set; } = 0.0f;
        public float Q3 { get; private set; } = 0.0f;

        public MadgwickAHRS(float samplePeriod, float beta = 0.1f)
        {
            SamplePeriod = samplePeriod;
            Beta = beta;
        }

        public void Update(float gx, float gy, float gz, float ax, float ay, float az)
        {
            float q0 = Q0, q1 = Q1, q2 = Q2, q3 = Q3;

            // Normalise accelerometer measurement
            float norm = (float)Math.Sqrt(ax * ax + ay * ay + az * az);
            if (norm == 0f) return; // Handle NaN
            ax /= norm;
            ay /= norm;
            az /= norm;

            // Gradient descent algorithm corrective step
            float _2q0 = 2f * q0;
            float _2q1 = 2f * q1;
            float _2q2 = 2f * q2;
            float _2q3 = 2f * q3;
            float _4q0 = 4f * q0;
            float _4q1 = 4f * q1;
            float _4q2 = 4f * q2;
            float _8q1 = 8f * q1;
            float _8q2 = 8f * q2;
            float q0q0 = q0 * q0;
            float q1q1 = q1 * q1;
            float q2q2 = q2 * q2;
            float q3q3 = q3 * q3;

            float s0 = _4q0 * q2q2 + _2q2 * ax + _4q0 * q1q1 - _2q1 * ay;
            float s1 = _4q1 * q3q3 - _2q3 * ax + 4f * q0q0 * q1 - _2q0 * ay - _4q1 + _8q1 * q1q1 + _8q1 * q2q2 + _4q1 * az;
            float s2 = 4f * q0q0 * q2 + _2q0 * ax + _4q2 * q3q3 - _2q3 * ay - _4q2 + _8q2 * q1q1 + _8q2 * q2q2 + _4q2 * az;
            float s3 = 4f * q1q1 * q3 - _2q1 * ax + 4f * q2q2 * q3 - _2q2 * ay;

            norm = (float)Math.Sqrt(s0 * s0 + s1 * s1 + s2 * s2 + s3 * s3);
            if (norm == 0f) return;
            s0 /= norm;
            s1 /= norm;
            s2 /= norm;
            s3 /= norm;

            // Integrate rate of change of quaternion
            float qDot0 = 0.5f * (-q1 * gx - q2 * gy - q3 * gz) - Beta * s0;
            float qDot1 = 0.5f * (q0 * gx + q2 * gz - q3 * gy) - Beta * s1;
            float qDot2 = 0.5f * (q0 * gy - q1 * gz + q3 * gx) - Beta * s2;
            float qDot3 = 0.5f * (q0 * gz + q1 * gy - q2 * gx) - Beta * s3;

            q0 += qDot0 * SamplePeriod;
            q1 += qDot1 * SamplePeriod;
            q2 += qDot2 * SamplePeriod;
            q3 += qDot3 * SamplePeriod;

            // Normalize quaternion
            norm = (float)Math.Sqrt(q0 * q0 + q1 * q1 + q2 * q2 + q3 * q3);
            if (norm == 0f) return;
            Q0 = q0 / norm;
            Q1 = q1 / norm;
            Q2 = q2 / norm;
            Q3 = q3 / norm;
        }

        public (float w, float x, float y, float z) Quaternion => (Q0, Q1, Q2, Q3);
    }

}
