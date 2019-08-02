using System.Linq;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using SimpleJSON;
using System;
using System.IO.Ports; 

namespace SerialCtrl {
    public class SerialTrial : MVRScript {
     
        // Name of plugin
        public static string pluginName = "Serial Controller";

        // Serial Variables
        private string[] portNames;
        private string portSelected = "None";
        private string baudSelected = "9600";
        protected JSONStorableFloat pulseFreq;
        private float pulsetime = 1;
        private string pulsestring = "";
		
        // Interface Controls
        protected UIDynamicButton startbutton;
        protected UIDynamicButton stopbutton;
        protected JSONStorableFloat positionY;
		protected JSONStorableFloat strokeOffset;
		protected JSONStorableFloat strokeModifier;
		protected JSONStorableFloat angleOffsetY;
		protected JSONStorableFloat angleModifierY;
		
        // Measurement Variables
        private Atom maleAtom;            // Male person
        private FreeControllerV3 penisBase;  // Penis base
        private Vector3 penisBaseTransform;              // Penis base position

        private Atom femaleAtom;           // Female person
        private FreeControllerV3 trackedObject; // Target point
        private Vector3 trackedObjectTransform;             // Target point position

        private FreeControllerV3 maleHip;
        private Vector3 maleHipTransform;
		

        private Vector3 highPosTransform;
        private Vector3 lowPosTransform;
		
		private float highLength;
		private float lowLength = 100.0f;
		private float adjHighLength;
		
		private float deltaLength;
		
        //private float strokeLength;
		private float penisLength;

		private float objectLocation;
		private float locationPercent;
		private float moveServo1;
		private float moveServo2;
		
		private string trackedObjectName;
		private float trackedObjectAngleY;
		private float trackedObjectAngleX;
		private float highAngleY;
		private float lowAngleY = 180.0f;
		private float midAngleY;
		private float angleCorrectionY;
		private float angleCorrectionX;

		private float sampleRate = 1;
		

        // Serial Stuff
        SerialPort serial;
        byte[] cmdByteArray = new byte[4];

        // Function to initiate plugin
        public override void Init() {
            try {
				pluginLabelJSON.val = "Serial Controller";
				// Check this is the right kind of atom
                if (containingAtom.type != "Person") {
                    SuperController.LogError($"Plugin must be added to a Person Atom, not '{containingAtom.type}'");
                    return;
                }
				femaleAtom = containingAtom;
                trackedObject = femaleAtom.GetStorableByID("hipControl") as FreeControllerV3; // Default Tracked Object
                maleAtom = SuperController.singleton.GetAtomByUid("Man");
                penisBase = maleAtom.GetStorableByID("penisBaseControl") as FreeControllerV3;
				maleHip = maleAtom.GetStorableByID("hipControl") as FreeControllerV3;
				//maleHipPlane = new plane(maleHip.transform.up,maleHip.transform.right);

				// Select Tracked Object - add additional options here if needed
                List<string> bodyParts = new List<string>();
                bodyParts.Add("hipControl");
                bodyParts.Add("pelvisControl");
                bodyParts.Add("lHandControl");
                bodyParts.Add("rHandControl");
                bodyParts.Add("headControl");
				//bodyParts.Add("someControl");  // example
                JSONStorableStringChooser bodyPartChooser = new JSONStorableStringChooser("Body Part Chooser", bodyParts, "hipControl", "Select body part", BodyPartChooserCallback);
                UIDynamicPopup bodyPartChooserPopup = CreatePopup(bodyPartChooser, false);
				bodyPartChooserPopup.labelWidth = 300f;
				
                // Select COM Port
                portNames = System.IO.Ports.SerialPort.GetPortNames();
                List<string> comPorts = new List<string>(portNames);
                JSONStorableStringChooser portChooser = new JSONStorableStringChooser("COM Port Chooser", comPorts, "None", "Select COM port", PortChooserCallback);
                UIDynamicPopup portChooserPopup = CreatePopup(portChooser, false);
				portChooserPopup.labelWidth = 300f;

/*                 // Select Baud Rate
                List<string> baudRates = new List<string>();
                baudRates.Add("9600");
                baudRates.Add("19200");
                baudRates.Add("38400");
                baudRates.Add("74880");
                baudRates.Add("115200");
                baudRates.Add("230400");
                baudRates.Add("250000");
                JSONStorableStringChooser baudChooser = new JSONStorableStringChooser("Baud Rate Chooser", baudRates, "9600", "Select baud rate", BaudChooserCallback);
                UIDynamicPopup baudChooserPopup = CreatePopup(baudChooser, false);
				baudChooserPopup.labelWidth = 300f; */
                
                // Start/Stop buttons
                startbutton = CreateButton("Start Serial", false);
                if (startbutton != null) {
                    startbutton.button.onClick.AddListener(StartButtonCallback);
                }
                stopbutton = CreateButton("Stop Serial", false);
                if (stopbutton != null) {
                    stopbutton.button.onClick.AddListener(StopButtonCallback);
                }
				
				// Select how often to update servo position in x times per second
                pulseFreq = new JSONStorableFloat("Serial Pulse Freq (/sec)", 50f, 1f, 100f, true, true);
                RegisterFloat(pulseFreq);
                CreateSlider(pulseFreq, false);
				
				// Stroke Offset - move the entire stroke up or down
                strokeOffset = new JSONStorableFloat("Stroke Offset", 0f, -30.0f, 30.0f, true, true);
                RegisterFloat(strokeOffset);
                CreateSlider(strokeOffset, false);
				
				// Stroke Length Modifier - Increase or Decrease total stroke length
                strokeModifier = new JSONStorableFloat("Stroke Length Modifer", 0f, -1f, 1f, true, true);
                RegisterFloat(strokeModifier);
                CreateSlider(strokeModifier, false);
				
				// Angle Offset - modify the center of rotation
                angleOffsetY = new JSONStorableFloat("Twist Offset Correction", 0f, -100f, 100f, true, true);
                RegisterFloat(angleOffsetY);
                CreateSlider(angleOffsetY, false);
				
				// Angle Modifier - Increase or Decrease the rotation amount compared to tracked object
                angleModifierY = new JSONStorableFloat("Rotation Angle Magnifier", 0f, -10f, 10f, true, true);
                RegisterFloat(angleModifierY);
                CreateSlider(angleModifierY, false);
				

            } catch (Exception e) {
                SuperController.LogError("Exception caught: " + e);
            }
        }

        // Update is called with each rendered frame by Unity
        void Update() {
            try 
			{
				// Stroking Code
				trackedObjectTransform = trackedObject.transform.position;  // Get the current position of the tracked object
                penisBaseTransform = penisBase.transform.position;  // Get the current position of the Penis Base
				deltaLength = Vector3.Distance(penisBaseTransform, trackedObjectTransform);  // Get the distance between Penis Base and Tracked Object as a float
				if (highLength < deltaLength) {
					highPosTransform = trackedObjectTransform;  //  Find the farthest point the tracked object is from the penis base
					highLength = deltaLength;  //  Find the length from penis base to the farthest distance
				}
					//adjHighLength = highLength - strokeModifier.val;  // calculate with Stroke modifier to adjust the total stroke length with the slider
					
/*                 if (lowLength > deltaLength) {
					lowPosTransform = trackedObjectTransform;  //  Find the nearest point the tracked object is from the penis base
					lowLength = deltaLength;  //  Find the shortest length
				} */
				
				objectLocation = Vector3.Distance(penisBaseTransform,trackedObjectTransform);  // Get distance between tracked object and penis base
				locationPercent = (objectLocation - strokeModifier.val) / (highLength - strokeModifier.val);  // location of tracked object as a percent of Penis Length
				
				// Side to Side Rotation 
				trackedObjectAngleY = penisBase.transform.localEulerAngles.y + angleOffsetY.val;  // Find the Local Angle of the Penis Base
 				if (trackedObjectAngleY > 180)  // convert the angle to a range -180 to 180
				{
					trackedObjectAngleY = (360 - trackedObjectAngleY) * -1;
					angleCorrectionY = trackedObjectAngleY;
				}
				else
				{
					angleCorrectionY = trackedObjectAngleY;
				}
					// Center the side to side Rotation
				if (highAngleY < trackedObjectAngleY)	// Find the most positive angle sweep
				{
					highAngleY = trackedObjectAngleY;
				}
				if (lowAngleY > trackedObjectAngleY)  // Find the most negative angle sweep
				{
					lowAngleY = trackedObjectAngleY;
				}
				midAngleY = (highAngleY + lowAngleY) / 2;   // find the mid point in the angle sweep and add that to the value sent to server to center the sweep
				angleCorrectionY = (angleCorrectionY + midAngleY) / (10-angleModifierY.val); // Angle Sent to Servo
				
//  LOGGING
				sampleRate -= Time.deltaTime;
                if (sampleRate <= 0f)
                {
                    sampleRate = 1/1;
					
					// GREEN Y - UP
					// RED X - Right
					// BLUE Z - Forward
                     
					//SuperController.LogMessage("trackedObjectAngleY = side2side = " + trackedObjectAngleY.ToString("f4"));
					//SuperController.LogMessage("angleCorrectionY = " + angleCorrectionY.ToString("f4"));
					//SuperController.LogMessage("trackedObjectAngleX = front2back = " + trackedObjectAngleX.ToString("f4"));
					//SuperController.LogMessage("angleFix = " + angleFix.ToString("f4"));
					//SuperController.LogMessage("deltaAngle = " + deltaAngle.ToString("f4"));
					//SuperController.LogMessage("adjAngleSweep = " + adjAngleSweep.ToString("f4"));
				}

                // If serial connection is open move servo
                if (serial != null && serial.IsOpen) 
				{
                    // Watch for next sample time and only move servo according to slider
                    pulsetime -= Time.deltaTime;
                    if (pulsetime <= 0f)
                    {
                        pulsetime = 1/pulseFreq.val;
                        MoveServo(locationPercent,angleCorrectionY);  // move server based on the percent of the total stroked length 
                    }
                }	
            } 
			catch (Exception e) 
			{
                SuperController.LogError("Exception caught: " + e);
            }
        }

        protected void BodyPartChooserCallback(string s) {
            trackedObject = femaleAtom.GetStorableByID(s) as FreeControllerV3;
			trackedObjectName = s;
			// Reset variables when tracked object is changed
			lowLength = 180.0f;
			lowAngleY = 180.0f;
			highLength = 0.0f;
			highAngleY = 0.0f;
        }

        protected void PortChooserCallback(string s) {
            portSelected = s;
        }
        protected void BaudChooserCallback(string s) {
            baudSelected = s;
        }
		
		// Function to Move the Servo to specific percent of the stroke
        private void MoveServo(float location, float angle) {

			if (location>100) location=100;  //clamp values
			if (location<0) location=0;
			if (angle>5) angle=5;
			if (angle<-5) angle=-5;
			
			moveServo1 = 32.0f * location;
			moveServo1 = 30 + moveServo1;

			moveServo1 = moveServo1 + strokeOffset.val + angle;

			if (moveServo1 > 62) moveServo1=62;  // clamp values
			if (moveServo1 < 30) moveServo1=30;
            byte servoPosition = Convert.ToByte(moveServo1);
			
			//build servo 0 command string
            cmdByteArray[0] = 0x84;  // Pololu Commands
            cmdByteArray[1] = 0x00;  // Servo on channel 0 identifier
            cmdByteArray[2] = 0x70;
            cmdByteArray[3] = servoPosition;  // command to position servo
			serial.Write(cmdByteArray,0,4);
			
			moveServo2 = 32.0f * location;
			moveServo2 = 62 - moveServo2;

			moveServo2 = moveServo2 - strokeOffset.val + angle;

			if (moveServo2 > 62) moveServo2=62;  // clamp values
			if (moveServo2 < 30) moveServo2=30;
            servoPosition = Convert.ToByte(moveServo2);
			
			//build servo 1 command string
			cmdByteArray[0] = 0x84;
            cmdByteArray[1] = 0x01;  // Servo on channel 1 identifier
            cmdByteArray[2] = 0x70;
            cmdByteArray[3] = servoPosition;  // command sets the servo position
			serial.Write(cmdByteArray,0,4);  // send command string to servo for movement
			
			// Servo Stroke Values in dec and hex
            // 62 or 3e = 2000
            // 46 or 2e = 1500
            // 30 or 1e = 0
			// max 10 point for rotation
			
            //SuperController.LogMessage("Moving Servo - " + servoPosition);
        }
		
		private float PosNegAngle(Vector3 a1, Vector3 a2, Vector3 normal)
		{
			float angle = Vector3.Angle(a1, a2);
			float sign = Mathf.Sign(Vector3.Dot(normal, Vector3.Cross(a1, a2)));
			return angle*sign;
		}
		

        // Serial button functions
        protected void StartButtonCallback() {
            StartSerial();
            //SuperController.LogMessage("Serial connection started: " + portSelected + ", baud " + baudSelected );
        }
        protected void StopButtonCallback() {
            StopSerial();
            //SuperController.LogMessage("Serial connection stopped");
        }
        // Function to start serial
        private void StartSerial() {
            if (portSelected != "None") {
                serial = new SerialPort(portSelected, 9600);
                serial.ReadTimeout = 10;
                serial.Open();
            }
        }
        // Function to stop serial
        private void StopSerial() {
            if(serial != null && serial.IsOpen) serial.Close();
        }

        // cleanup
        private void OnDestroy() {
            try {
                StopSerial();
            } catch (Exception e) {
                SuperController.LogError("Exception caught: " + e);
            }
        }


    }
}