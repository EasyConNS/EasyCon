namespace EasyCon2.Helper
{
    public class MouseTranslation
    {
        public (double X, double Y) mc_sensitivity = (0.5, 0.5);
        public (double X, double Y) mc_sensitivity2 = (0.5, 0.5);
        public (double X, double Y) mc_mouse_delta_start_threshold = (0.0, 0.0);
        public (double X, double Y) mc_delta_sensitivity_sigmoid_constant = (-0.5, -0.5);
        public (double X, double Y) mc_delta_initial = (128 * 0.5, 128 * 0.5);
        public (double X, double Y) mc_delta_stop_threshold = (128 * 0.49, 128 * 0.49);
        public (double X, double Y) mc_delta_damping_origin = (128 * 0.4, 128 * 0.4);
        public (double X, double Y) mc_delta_damping = (0.0, 0.0);
        public (double X, double Y) mc_delta_damping2 = (0.1, 0.1);
        public (double X, double Y) mc_delta_damping_sigmoid_constant = (0.5, 0.5);
        public (double X, double Y) mc_delta_max = (128 * 2.0, 128 * 2.0);
        public (double X, double Y) mc_delta = (0, 0);
        public (double X, double Y) mc_mouse_delta = (0, 0);
        public (double X, double Y) mc_mouse = (0, 0);

        public MouseTranslation()
        {
            mc_delta_damping.X = 1.0 - mc_delta_damping.X;
            mc_delta_damping.Y = 1.0 - mc_delta_damping.Y;
            mc_delta_damping2.X = 1.0 - mc_delta_damping2.X;
            mc_delta_damping2.Y = 1.0 - mc_delta_damping2.Y;
        }

        private double Sigmoid_Tunable(double k, double x)
        {
            return (x - x * k) / (k - Math.Abs(x) * 2.0 * k + 1.0);
        }

        public System.Drawing.Point Translate(System.Drawing.Point mouse, System.Drawing.Point center)
        {
            mc_mouse = (mouse.X, mouse.Y);
            mc_mouse_delta = (mc_mouse.X - center.X, mc_mouse.Y - center.Y);

            if (mc_delta.X == 0.0 && Math.Abs(mc_mouse_delta.X) > mc_mouse_delta_start_threshold.X)
                mc_delta.X = mc_delta_initial.X * Math.Abs(mc_mouse_delta.X) / mc_mouse_delta.X;
            if (mc_delta.Y == 0.0 && Math.Abs(mc_mouse_delta.Y) > mc_mouse_delta_start_threshold.Y)
                mc_delta.Y = mc_delta_initial.Y * Math.Abs(mc_mouse_delta.Y) / mc_mouse_delta.Y;

            if (mc_delta.X != 0.0)
                mc_delta.X += mc_mouse_delta.X * (mc_sensitivity.X + mc_sensitivity2.X * Math.Abs(Sigmoid_Tunable(mc_delta_sensitivity_sigmoid_constant.X, mc_delta.X / mc_delta_max.X)));
            if (mc_delta.Y != 0.0)
                mc_delta.Y += mc_mouse_delta.Y * (mc_sensitivity.Y + mc_sensitivity2.Y * Math.Abs(Sigmoid_Tunable(mc_delta_sensitivity_sigmoid_constant.Y, mc_delta.Y / mc_delta_max.Y)));

            if (Math.Abs(mc_delta.X) > mc_delta_max.X)
                mc_delta.X = Math.Abs(mc_delta.X) / mc_delta.X * mc_delta_max.X;
            if (Math.Abs(mc_delta.Y) > mc_delta_max.Y)
                mc_delta.Y = Math.Abs(mc_delta.Y) / mc_delta.Y * mc_delta_max.Y;

            var translation = new System.Drawing.Point((int)(mc_delta.X + 128), (int)(mc_delta.Y + 128));

            if (mc_delta.X != 0)
            {
                var dt_x = mc_delta_damping_origin.X * Math.Abs(mc_delta.X) / mc_delta.X;
                mc_delta.X = (mc_delta.X - dt_x) * (mc_delta_damping.X + mc_delta_damping2.X * Math.Abs(Sigmoid_Tunable(mc_delta_damping_sigmoid_constant.X, mc_delta.X / mc_delta_max.X))) * 0.5 + dt_x;
            }
            if (mc_delta.Y != 0)
            {
                var dt_y = mc_delta_damping_origin.Y * Math.Abs(mc_delta.Y) / mc_delta.Y;
                mc_delta.Y = (mc_delta.Y - dt_y) * (mc_delta_damping.Y + mc_delta_damping2.Y * Math.Abs(Sigmoid_Tunable(mc_delta_damping_sigmoid_constant.Y, mc_delta.Y / mc_delta_max.Y))) * 0.5 + dt_y;
            }
            if (Math.Abs(mc_delta.X) < mc_delta_stop_threshold.X)
                mc_delta.X = 0.0;
            if (Math.Abs(mc_delta.Y) < mc_delta_stop_threshold.Y)
                mc_delta.Y = 0.0;
            return translation;
        }
    }
}