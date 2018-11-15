﻿using MathSupport;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using Scene3D;
using System;
using System.Collections.Generic;
using System.Windows.Forms;
using OpenglSupport;
using System.Drawing;
using System.Globalization;


namespace Rendering
{
  public partial class RayVisualizerForm: Form
  {
    public static RayVisualizerForm singleton; //singleton

    /// <summary>
    /// Scene read from file.
    /// </summary>
    private readonly SceneBrep scene = new SceneBrep ();

    /// <summary>
    /// Scene center point.
    /// </summary>
    private Vector3 center = Vector3.Zero;

    /// <summary>
    /// Scene diameter.
    /// </summary>
    private float diameter = 4.0f;

    private const float near = 0.1f;
    private const float far = float.PositiveInfinity;

    private Vector3 light = new Vector3 ( -2, 1, 1 );

    /// <summary>
    /// GLControl guard flag.
    /// </summary>
    private bool loaded = false;

    /// <summary>
    /// Associated Trackball instance.
    /// </summary>
    internal Trackball trackBall = null;

    /// <summary>
    /// Frustum vertices, 0 or 8 vertices
    /// </summary>
    private readonly List<Vector3> frustumFrame = new List<Vector3> ();

    /// <summary>
    /// Point in the 3D scene pointed out by an user, or null.
    /// </summary>
    private Vector3? spot = null;

    private Vector3? pointOrigin = null;
    private Vector3  pointTarget;
    private Vector3  eye;

    private bool pointDirty = false;

    /// <summary>
    /// Are we allowed to use VBO?
    /// </summary>
    private bool useVBO = true;

    /// <summary>
    /// Can we use shaders?
    /// </summary>
    private bool canShaders = false;

    /// <summary>
    /// Are we currently using shaders?
    /// </summary>
    private bool useShaders = false;

    private uint[] VBOid  = null; // vertex array (colors, normals, coords), index array
    private int    stride = 0;    // stride for vertex array

    /// <summary>
    /// Current texture.
    /// </summary>
    private int texName = 0;

    /// <summary>
    /// Global GLSL program repository.
    /// </summary>
    private Dictionary<string, GlProgramInfo> programs = new Dictionary<string, GlProgramInfo> ();

    /// <summary>
    /// Current (active) GLSL program.
    /// </summary>
    private GlProgram activeProgram = null;

    // appearance:
    private Vector3 globalAmbient = new Vector3 ( 0.2f, 0.2f, 0.2f );
    private Vector3 matAmbient    = new Vector3 ( 0.8f, 0.6f, 0.2f );
    private Vector3 matDiffuse    = new Vector3 ( 0.8f, 0.6f, 0.2f );
    private Vector3 matSpecular   = new Vector3 ( 0.8f, 0.8f, 0.8f );
    private float   matShininess  = 100.0f;
    private Vector3 whiteLight    = new Vector3 ( 1.0f, 1.0f, 1.0f );
    private Vector3 lightPosition = new Vector3 ( -20.0f, 10.0f, 10.0f );

    private long   lastFPSTime     = 0L;
    private int    frameCounter    = 0;
    private long   triangleCounter = 0L;
    private double lastFPS         = 0.0;
    private double lastTPS         = 0.0;

    private Color defaultBackgroundColor = Color.Black;

    public RayVisualizerForm ()
    {
      InitializeComponent ();

      Form1.singleton.RayVisualiserButton.Enabled = false;

      trackBall = new Trackball ( center, diameter );

      InitShaderRepository ();

      singleton = this;

      RayVisualizer.singleton = new RayVisualizer ();

      Cursor.Current = Cursors.Default;

      if ( AdditionalViews.singleton.pointCloud == null || AdditionalViews.singleton.pointCloud.IsCloudEmpty )
        PointCloudButton.Enabled = false;
      else
        PointCloudButton.Enabled = true;
    }

    private void glControl1_Load ( object sender, EventArgs e )
    {
      InitOpenGL ();

      trackBall.GLsetupViewport ( glControl1.Width, glControl1.Height, near, far );

      loaded = true;

      Application.Idle += new EventHandler ( Application_Idle );
    }

    private void glControl1_Resize ( object sender, EventArgs e )
    {
      if ( !loaded )
        return;

      trackBall.GLsetupViewport ( glControl1.Width, glControl1.Height, near, far );

      glControl1.Invalidate ();
    }

    private void glControl1_Paint ( object sender, PaintEventArgs e )
    {
      Render ();
    }

    private void RayVisualizerForm_FormClosing ( object sender, FormClosingEventArgs e )
    {
      if ( VBOid != null && VBOid [ 0 ] != 0 )
      {
        GL.DeleteBuffers ( 2, VBOid );
        VBOid = null;
      }

      DestroyShaders ();
    }

    /// <summary>
    /// Unproject support
    /// </summary>
    private Vector3 screenToWorld ( int x, int y, float z = 0.0f )
    {
      GL.GetFloat ( GetPName.ModelviewMatrix, out Matrix4 modelViewMatrix );
      GL.GetFloat ( GetPName.ProjectionMatrix, out Matrix4 projectionMatrix );

      return Geometry.UnProject ( ref projectionMatrix, ref modelViewMatrix, glControl1.Width, glControl1.Height, x,
                                  glControl1.Height - y, z );
    }

    private void glControl1_MouseDown ( object sender, MouseEventArgs e )
    {
      if ( !trackBall.MouseDown ( e ) )
        if ( checkAxes.Checked )
        {
          // pointing to the scene:
          pointOrigin = screenToWorld ( e.X, e.Y, 0.0f );
          pointTarget = screenToWorld ( e.X, e.Y, 1.0f );

          eye        = trackBall.Eye;
          pointDirty = true;
        }
    }

    private void glControl1_MouseUp ( object sender, MouseEventArgs e )
    {
      trackBall.MouseUp ( e );
    }

    private void glControl1_MouseMove ( object sender, MouseEventArgs e )
    {
      if ( AllignCameraCheckBox.Checked && e.Button == trackBall.Button )
      {
        MessageBox.Show ( @"You can not use mouse to rotate scene while ""Keep alligned"" box is checked." );
        return;
      }

      trackBall.MouseMove ( e );
    }

    private void glControl1_MouseWheel ( object sender, MouseEventArgs e )
    {
      trackBall.MouseWheel ( e );
    }

    private void glControl1_KeyDown ( object sender, KeyEventArgs e )
    {
      trackBall.KeyDown ( e );
    }

    private void glControl1_KeyUp ( object sender, KeyEventArgs e )
    {
      if ( !trackBall.KeyUp ( e ) )
      {
        if ( e.KeyCode == Keys.F )
        {
          e.Handled = true;
          if ( frustumFrame.Count > 0 )
            frustumFrame.Clear ();
          else
          {
            float N = 0.0f;
            float F = 1.0f;
            int   R = glControl1.Width - 1;
            int   B = glControl1.Height - 1;
            frustumFrame.Add ( screenToWorld ( 0, 0, N ) );
            frustumFrame.Add ( screenToWorld ( R, 0, N ) );
            frustumFrame.Add ( screenToWorld ( 0, B, N ) );
            frustumFrame.Add ( screenToWorld ( R, B, N ) );
            frustumFrame.Add ( screenToWorld ( 0, 0, F ) );
            frustumFrame.Add ( screenToWorld ( R, 0, F ) );
            frustumFrame.Add ( screenToWorld ( 0, B, F ) );
            frustumFrame.Add ( screenToWorld ( R, B, F ) );
          }
        }
      }
    }

    private void RayVisualizerForm_FormClosed ( object sender, FormClosedEventArgs e )
    {
      Form1.singleton.RayVisualiserButton.Enabled = true;

      singleton = null;
    }

    /// <summary>
    /// Moves camera so that primary ray is perpendicular to screen
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void AllignCamera ( object sender, EventArgs e )
    {
      if ( RayVisualizer.singleton?.rays.Count < 2 )
        return;

      trackBall.Center = (Vector3) RayVisualizer.singleton.rays [ 1 ];
      trackBall.Reset ( (Vector3) ( RayVisualizer.singleton.rays [ 1 ] - RayVisualizer.singleton.rays [ 0 ] ) );

      double distanceOfEye = Vector3.Distance ( trackBall.Center, trackBall.Eye );
      double distanceOfCamera = Vector3d.Distance ( RayVisualizer.singleton.rays [ 1 ], RayVisualizer.singleton.rays [ 0 ]) * 0.9;

      trackBall.Zoom = (float) (distanceOfEye / distanceOfCamera);
    }

    private void InitOpenGL ()
		{
			// log OpenGL info
			GlInfo.LogGLProperties ();

			// general OpenGL
			glControl1.VSync = true;
			GL.ClearColor ( Color.Black );
			GL.Enable ( EnableCap.DepthTest );
			GL.ShadeModel ( ShadingModel.Flat );

			// VBO initialization
			VBOid = new uint[2];
			GL.GenBuffers ( 2, VBOid );
			useVBO = ( GL.GetError () == ErrorCode.NoError );

			// shaders
			if ( useVBO )
				canShaders = SetupShaders ();
		}

		/// <summary>
		/// Init shaders registered in global repository 'programs'.
		/// </summary>
		/// <returns>True if succeeded.</returns>
		private bool SetupShaders ()
		{
			activeProgram = null;

			foreach ( var programInfo in programs.Values )
				if ( programInfo.Setup () )
					activeProgram = programInfo.program;

			if ( activeProgram == null )
				return false;

			if ( programs.TryGetValue ( "default", out GlProgramInfo defInfo ) && defInfo.program != null )
				activeProgram = defInfo.program;

			return true;
		}

		/// <summary>
		/// Set light-source coordinate in the world-space.
		/// </summary>
		/// <param name="size">Relative size (based on the scene size).</param>
		/// <param name="light">Relative light position (default=[-2,1,1],viewer=[0,0,1]).</param>
		private void SetLight ( float size, ref Vector3 light )
		{
			lightPosition = 2.0f * size * light;
		}

    private void InitShaderRepository ()
		{
			programs.Clear ();

      // default program:
		  GlProgramInfo pi = new GlProgramInfo ( "default", new GlShaderInfo[]
			{
		    new GlShaderInfo ( ShaderType.VertexShader, "vertex.glsl", "048rtmontecarlo-script" ),
		    new GlShaderInfo ( ShaderType.FragmentShader, "fragment.glsl", "048rtmontecarlo-script" )
			} );

			programs[pi.name] = pi;
		}

    private void DestroyShaders ()
		{
			foreach ( GlProgramInfo prg in programs.Values )
				prg.Destroy ();
		}

    private void Render ()
		{
			if ( !loaded )
				return;

		  Color backgroundColor;

      if ( RayVisualizer.backgroundColor == null )
		    backgroundColor = defaultBackgroundColor;
		  else
        backgroundColor = Color.FromArgb ( (int) ( RayVisualizer.backgroundColor [0] * 255 ),
                                           (int) ( RayVisualizer.backgroundColor [1] * 255 ),
                                           (int) ( RayVisualizer.backgroundColor [2] * 255 ) );

		  GL.ClearColor ( backgroundColor );

			frameCounter++;
			useShaders = ( scene != null ) &&
						 useVBO &&
						 canShaders &&
						 activeProgram != null &&
						 checkShaders.Checked;

			GL.Clear ( ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit );
			GL.ShadeModel ( checkSmooth.Checked ? ShadingModel.Smooth : ShadingModel.Flat );
			GL.PolygonMode ( checkTwosided.Checked ? MaterialFace.FrontAndBack : MaterialFace.Front,
							 checkWireframe.Checked ? PolygonMode.Line : PolygonMode.Fill );

			if ( checkTwosided.Checked )
				GL.Disable ( EnableCap.CullFace );
			else
				GL.Enable ( EnableCap.CullFace );

			trackBall.GLsetCamera ();
			RenderScene ();

			glControl1.SwapBuffers ();
		}

    private void Application_Idle ( object sender, EventArgs e )
		{
			if ( glControl1.IsDisposed )
				return;

			while ( glControl1.IsIdle )
			{
#if USE_INVALIDATE
        glControl1.Invalidate ();
#else
				glControl1.MakeCurrent ();
				Render ();
#endif

				long now = DateTime.Now.Ticks;
				if ( now - lastFPSTime > 5000000 ) // more than 0.5 sec
				{
					lastFPS = 0.5 * lastFPS + 0.5 * ( frameCounter * 1.0e7 / ( now - lastFPSTime ) );
					lastTPS = 0.5 * lastTPS + 0.5 * ( triangleCounter * 1.0e7 / ( now - lastFPSTime ) );
					lastFPSTime = now;
					frameCounter = 0;
					triangleCounter = 0L;

					if ( lastTPS < 5.0e5 )
						labelFPS.Text = string.Format ( CultureInfo.InvariantCulture, "FPS: {0:f1}, TPS: {1:f0}k",
														lastFPS, ( lastTPS * 1.0e-3 ) );
					else
						labelFPS.Text = string.Format ( CultureInfo.InvariantCulture, "FPS: {0:f1}, TPS: {1:f1}m",
														lastFPS, ( lastTPS * 1.0e-6 ) );
				}

				// pointing:
				if ( pointOrigin != null &&
					 pointDirty )
				{
					Vector3d p0 = new Vector3d ( pointOrigin.Value.X, pointOrigin.Value.Y, pointOrigin.Value.Z );
					Vector3d p1 = new Vector3d ( pointTarget.X, pointTarget.Y, pointTarget.Z ) - p0;
					Vector2d uv;
					double   nearest = double.PositiveInfinity;

					if ( scene != null && scene.Triangles > 0 )
					{
						for ( int i = 0; i < scene.Triangles; i++ )
						{
							scene.GetTriangleVertices ( i, out Vector3 A, out Vector3 B, out Vector3 C );

							double curr = Geometry.RayTriangleIntersection ( ref p0, ref p1, ref A, ref B, ref C, out uv );

							if ( !double.IsInfinity ( curr ) && curr < nearest )
								nearest = curr;
						}
					}
					else
					{
						Vector3d ul   = new Vector3d ( -1.0, -1.0, -1.0 );
						Vector3d size = new Vector3d ( 2.0, 2.0, 2.0 );

						if ( Geometry.RayBoxIntersection ( ref p0, ref p1, ref ul, ref size, out uv ) )
							nearest = uv.X;
					}

					if ( double.IsInfinity ( nearest ) )
						spot = null;
					else
						spot = new Vector3 ( (float) ( p0.X + nearest * p1.X ),
											 (float) ( p0.Y + nearest * p1.Y ),
											 (float) ( p0.Z + nearest * p1.Z ) );

					pointDirty = false;
				}
			}
		}

		// attribute/vertex arrays:
    private bool vertexAttribOn  = false;
    private bool vertexPointerOn = false;

		private void SetVertexAttrib ( bool on )
		{
			if ( vertexAttribOn == on )
				return;

			if ( activeProgram != null )
				if ( on )
					activeProgram.EnableVertexAttribArrays ();
				else
					activeProgram.DisableVertexAttribArrays ();

			vertexAttribOn = on;
		}

		private void SetVertexPointer ( bool on )
		{
			if ( vertexPointerOn == on )
				return;

			if ( on )
			{
				GL.EnableClientState ( ArrayCap.VertexArray );

				if ( scene.TxtCoords > 0 )
					GL.EnableClientState ( ArrayCap.TextureCoordArray );

				if ( scene.Normals > 0 )
					GL.EnableClientState ( ArrayCap.NormalArray );

				if ( scene.Colors > 0 )
					GL.EnableClientState ( ArrayCap.ColorArray );
			}
			else
			{
				GL.DisableClientState ( ArrayCap.VertexArray );
				GL.DisableClientState ( ArrayCap.TextureCoordArray );
				GL.DisableClientState ( ArrayCap.NormalArray );
				GL.DisableClientState ( ArrayCap.ColorArray );
			}

			vertexPointerOn = on;
		}

		/// <summary>
		/// Rendering code itself (separated for clarity).
		/// </summary>
		private void RenderScene ()
		{
      // Scene rendering:
      if ( useShaders )
      {
        bool renderFirst = true;

        if ( AllignCameraCheckBox.Checked )
        {
          AllignCamera ( null, null );
          renderFirst = false;
        }

        //FillSceneObjects ();
        //BoundingBoxesVisualization ();

        RenderRays ( renderFirst );
        RenderCamera ();
        RenderLightSources ();



        SetVertexPointer ( false );
        SetVertexAttrib ( true );

        // using GLSL shaders:
        GL.UseProgram ( activeProgram.Id );

        // uniforms:
        Matrix4 modelView  = trackBall.ModelView;
        Matrix4 projection = trackBall.Projection;
        Vector3 localEye   = trackBall.Eye;

        GL.UniformMatrix4 ( activeProgram.GetUniform ( "matrixModelView" ), false, ref modelView );
        GL.UniformMatrix4 ( activeProgram.GetUniform ( "matrixProjection" ), false, ref projection );

        GL.Uniform3 ( activeProgram.GetUniform ( "globalAmbient" ), ref globalAmbient );
        GL.Uniform3 ( activeProgram.GetUniform ( "lightColor" ), ref whiteLight );
        GL.Uniform3 ( activeProgram.GetUniform ( "lightPosition" ), ref lightPosition );
        GL.Uniform3 ( activeProgram.GetUniform ( "eyePosition" ), ref localEye );
        GL.Uniform3 ( activeProgram.GetUniform ( "Ka" ), ref matAmbient );
        GL.Uniform3 ( activeProgram.GetUniform ( "Kd" ), ref matDiffuse );
        GL.Uniform3 ( activeProgram.GetUniform ( "Ks" ), ref matSpecular );
        GL.Uniform1 ( activeProgram.GetUniform ( "shininess" ), matShininess );

        // color handling:
        bool useGlobalColor = checkGlobalColor.Checked;
        GL.Uniform1 ( activeProgram.GetUniform ( "globalColor" ), useGlobalColor ? 1 : 0 );

        // shading:
        bool shadingPhong = checkPhong.Checked;
        bool shadingGouraud = checkSmooth.Checked;

        if ( !shadingGouraud )
          shadingPhong = false;

        GL.Uniform1 ( activeProgram.GetUniform ( "shadingPhong" ), shadingPhong ? 1 : 0 );
        GL.Uniform1 ( activeProgram.GetUniform ( "shadingGouraud" ), shadingGouraud ? 1 : 0 );
        GL.Uniform1 ( activeProgram.GetUniform ( "useAmbient" ), checkAmbient.Checked ? 1 : 0 );
        GL.Uniform1 ( activeProgram.GetUniform ( "useDiffuse" ), checkDiffuse.Checked ? 1 : 0 );
        GL.Uniform1 ( activeProgram.GetUniform ( "useSpecular" ), checkSpecular.Checked ? 1 : 0 );
        GlInfo.LogError ( "set-uniforms" );

        const int pointCloudVBOStride = 9 * sizeof ( float );       

        if ( pointCloudVBO != 0 && PointCloudCheckBox.Checked )
        {
          GL.BindBuffer ( BufferTarget.ArrayBuffer, pointCloudVBO );

          // positions
          GL.VertexAttribPointer ( activeProgram.GetAttribute ( "position" ), 3, VertexAttribPointerType.Float, false, pointCloudVBOStride, (IntPtr) 0 );
          //GL.EnableVertexAttribArray ( activeProgram.GetAttribute ( "position" ) );

          // colors
          if ( activeProgram.HasAttribute ( "color" ) )
          {
            GL.VertexAttribPointer ( activeProgram.GetAttribute ( "color" ), 3, VertexAttribPointerType.Float, false, pointCloudVBOStride,
                                     (IntPtr) ( 3 * sizeof ( float ) ) );
            //GL.EnableVertexAttribArray ( activeProgram.GetAttribute ( "color" ) );
          }

          // normals
          if ( activeProgram.HasAttribute ( "normal" ) )
          {
            GL.VertexAttribPointer ( activeProgram.GetAttribute ( "normal" ), 3, VertexAttribPointerType.Float, false, pointCloudVBOStride,
                                     (IntPtr) ( 6 * sizeof ( float ) ) );
            //GL.EnableVertexAttribArray ( activeProgram.GetAttribute ( "normal" ) );
          }

          GlInfo.LogError ( "set-attrib-pointers" );

          GL.DrawArrays ( PrimitiveType.Points, 0, pointCloud.numberOfElements );
        }
        else
        {
          //throw new NotImplementedException ();
        }

        // cleanup:
        GL.UseProgram ( 0 );
      }


      // Support: axes
      if ( checkAxes.Checked )
			{
				float origWidth = GL.GetFloat ( GetPName.LineWidth );
				float origPoint = GL.GetFloat ( GetPName.PointSize );

				// axes:
				RenderAxes ();

				// Support: pointing
				if ( pointOrigin != null )
				{
					RenderPointing ();
				}

				// Support: frustum
				if ( frustumFrame.Count >= 8 )
				{
					RenderFrustum ();
				}

				GL.LineWidth ( origWidth );
				GL.PointSize ( origPoint );
			}
		}

		private void RenderPointing ()
		{
			GL.Begin ( PrimitiveType.Lines );
			GL.Color3 ( 1.0f, 1.0f, 0.0f );
			GL.Vertex3 ( pointOrigin.Value );
			GL.Vertex3 ( pointTarget );
			GL.Color3 ( 1.0f, 0.0f, 0.0f );
			GL.Vertex3 ( pointOrigin.Value );
			GL.Vertex3 ( eye );
			GL.End ();

			GL.PointSize ( 4.0f );
			GL.Begin ( PrimitiveType.Points );
			GL.Color3 ( 1.0f, 0.0f, 0.0f );
			GL.Vertex3 ( pointOrigin.Value );
			GL.Color3 ( 0.0f, 1.0f, 0.2f );
			GL.Vertex3 ( pointTarget );
			GL.Color3 ( 1.0f, 1.0f, 1.0f );

			if ( spot != null )
				GL.Vertex3 ( spot.Value );

			GL.Vertex3 ( eye );
			GL.End ();
		}

		private void RenderFrustum ()
		{
			GL.LineWidth ( 2.0f );
			GL.Begin ( PrimitiveType.Lines );

			GL.Color3 ( 1.0f, 0.0f, 0.0f );
			GL.Vertex3 ( frustumFrame[0] );
			GL.Vertex3 ( frustumFrame[1] );
			GL.Vertex3 ( frustumFrame[1] );
			GL.Vertex3 ( frustumFrame[3] );
			GL.Vertex3 ( frustumFrame[3] );
			GL.Vertex3 ( frustumFrame[2] );
			GL.Vertex3 ( frustumFrame[2] );
			GL.Vertex3 ( frustumFrame[0] );

			GL.Color3 ( 1.0f, 1.0f, 1.0f );
			GL.Vertex3 ( frustumFrame[0] );
			GL.Vertex3 ( frustumFrame[4] );
			GL.Vertex3 ( frustumFrame[1] );
			GL.Vertex3 ( frustumFrame[5] );
			GL.Vertex3 ( frustumFrame[2] );
			GL.Vertex3 ( frustumFrame[6] );
			GL.Vertex3 ( frustumFrame[3] );
			GL.Vertex3 ( frustumFrame[7] );

			GL.Color3 ( 0.0f, 1.0f, 0.0f );
			GL.Vertex3 ( frustumFrame[4] );
			GL.Vertex3 ( frustumFrame[5] );
			GL.Vertex3 ( frustumFrame[5] );
			GL.Vertex3 ( frustumFrame[7] );
			GL.Vertex3 ( frustumFrame[7] );
			GL.Vertex3 ( frustumFrame[6] );
			GL.Vertex3 ( frustumFrame[6] );
			GL.Vertex3 ( frustumFrame[4] );

			GL.End ();
		}

		private void RenderAxes ()
		{
			GL.LineWidth ( 2.0f );
			GL.Begin ( PrimitiveType.Lines );

			GL.Color3 ( 1.0f, 0.1f, 0.1f );
			GL.Vertex3 ( center );
			GL.Vertex3 ( center + new Vector3 ( 1.5f, 0.0f, 0.0f ) * diameter );

			GL.Color3 ( 0.0f, 1.0f, 0.0f );
			GL.Vertex3 ( center );
			GL.Vertex3 ( center + new Vector3 ( 0.0f, 1.5f, 0.0f ) * diameter );

			GL.Color3 ( 0.2f, 0.2f, 1.0f );
			GL.Vertex3 ( center );
			GL.Vertex3 ( center + new Vector3 ( 0.0f, 0.0f, 1.5f ) * diameter );

			GL.End ();
		}

		private void RenderPlaceholderScene ()
		{
			SetVertexPointer ( false );
			SetVertexAttrib ( false );

			GL.Begin ( PrimitiveType.Quads );

			GL.Color3 ( 0.0f, 1.0f, 0.0f );    // Set The Color To Green
			GL.Vertex3 ( 1.0f, 1.0f, -1.0f );  // Top Right Of The Quad (Top)
			GL.Vertex3 ( -1.0f, 1.0f, -1.0f ); // Top Left Of The Quad (Top)
			GL.Vertex3 ( -1.0f, 1.0f, 1.0f );  // Bottom Left Of The Quad (Top)
			GL.Vertex3 ( 1.0f, 1.0f, 1.0f );   // Bottom Right Of The Quad (Top)

			GL.Color3 ( 1.0f, 0.5f, 0.0f );     // Set The Color To Orange
			GL.Vertex3 ( 1.0f, -1.0f, 1.0f );   // Top Right Of The Quad (Bottom)
			GL.Vertex3 ( -1.0f, -1.0f, 1.0f );  // Top Left Of The Quad (Bottom)
			GL.Vertex3 ( -1.0f, -1.0f, -1.0f ); // Bottom Left Of The Quad (Bottom)
			GL.Vertex3 ( 1.0f, -1.0f, -1.0f );  // Bottom Right Of The Quad (Bottom)

			GL.Color3 ( 1.0f, 0.0f, 0.0f );    // Set The Color To Red
			GL.Vertex3 ( 1.0f, 1.0f, 1.0f );   // Top Right Of The Quad (Front)
			GL.Vertex3 ( -1.0f, 1.0f, 1.0f );  // Top Left Of The Quad (Front)
			GL.Vertex3 ( -1.0f, -1.0f, 1.0f ); // Bottom Left Of The Quad (Front)
			GL.Vertex3 ( 1.0f, -1.0f, 1.0f );  // Bottom Right Of The Quad (Front)

			GL.Color3 ( 1.0f, 1.0f, 0.0f );     // Set The Color To Yellow
			GL.Vertex3 ( 1.0f, -1.0f, -1.0f );  // Bottom Left Of The Quad (Back)
			GL.Vertex3 ( -1.0f, -1.0f, -1.0f ); // Bottom Right Of The Quad (Back)
			GL.Vertex3 ( -1.0f, 1.0f, -1.0f );  // Top Right Of The Quad (Back)
			GL.Vertex3 ( 1.0f, 1.0f, -1.0f );   // Top Left Of The Quad (Back)

			GL.Color3 ( 0.0f, 0.0f, 1.0f );     // Set The Color To Blue
			GL.Vertex3 ( -1.0f, 1.0f, 1.0f );   // Top Right Of The Quad (Left)
			GL.Vertex3 ( -1.0f, 1.0f, -1.0f );  // Top Left Of The Quad (Left)
			GL.Vertex3 ( -1.0f, -1.0f, -1.0f ); // Bottom Left Of The Quad (Left)
			GL.Vertex3 ( -1.0f, -1.0f, 1.0f );  // Bottom Right Of The Quad (Left)

			GL.Color3 ( 1.0f, 0.0f, 1.0f );    // Set The Color To Violet
			GL.Vertex3 ( 1.0f, 1.0f, -1.0f );  // Top Right Of The Quad (Right)
			GL.Vertex3 ( 1.0f, 1.0f, 1.0f );   // Top Left Of The Quad (Right)
			GL.Vertex3 ( 1.0f, -1.0f, 1.0f );  // Bottom Left Of The Quad (Right)
			GL.Vertex3 ( 1.0f, -1.0f, -1.0f ); // Bottom Right Of The Quad (Right)

			GL.End ();

			triangleCounter += 12;
		}

		/// <summary>
		/// Renders all normal and shadow rays (further selection done via check boxes)
		/// </summary>
		private void RenderRays ( bool renderFirst )
		{
			SetVertexPointer ( false );
			SetVertexAttrib ( false );

			GL.LineWidth ( 2.0f );

			GL.Begin ( PrimitiveType.Lines );

			if ( NormalRaysCheckBox.Checked ) // Render normal rays
			{
				int offset = 0;

				if ( !renderFirst )
				{
					offset = 2;
				}

				GL.Color3 ( Color.Red );
				for ( int i = offset; i < RayVisualizer.singleton.rays.Count; i += 2 )
				{
					GL.Vertex3 ( RayVisualizer.singleton.rays[i] );
					GL.Vertex3 ( RayVisualizer.singleton.rays[i + 1] );
				}
			}

			if ( ShadowRaysCheckBox.Checked ) // Render shadow rays
			{
				GL.Color3 ( Color.Yellow );
				for ( int i = 0; i < RayVisualizer.singleton.shadowRays.Count; i += 2 )
				{
					GL.Vertex3 ( RayVisualizer.singleton.shadowRays[i] );
					GL.Vertex3 ( RayVisualizer.singleton.shadowRays[i + 1] );
				}
			}

			GL.End ();
		}

		/// <summary>
		/// Renders representation of camera (initially at position of rayOrigin of first primary ray)
		/// </summary>
		private void RenderCamera ()
		{
			if ( RayVisualizer.singleton.rays.Count == 0 || !CameraCheckBox.Checked )
				return;

			RenderCube ( RayVisualizer.singleton.rays[0], 0.2f, Color.Turquoise );
		}

		/// <summary>
		/// Renders representation of all light sources (except those in with null as position - usually ambient and directional lights which position does not matter)
		/// </summary>
		private void RenderLightSources ()
		{
			if ( /*RayVisualizer.singleton?.rays.Count == 0 || */ rayScene?.Sources == null || !LightSourcesCheckBox.Checked )
				return;

			foreach ( ILightSource lightSource in rayScene.Sources )
			{
				if ( lightSource.position != null )
					RenderCube ( RayVisualizer.AxesCorrector ( lightSource.position ), 0.07f, Color.Yellow );
			}
		}

		/// <summary>
		/// Renders simple cube of uniform color
		/// Initially used as placeholder so several objects
		/// </summary>
		/// <param name="position">Position in space</param>
		/// <param name="size">Size of cube</param>
		/// <param name="color">Uniform color of cube</param>
		private void RenderCube ( Vector3d position, float size, Color color )
		{
			SetVertexPointer ( false );
			SetVertexAttrib ( false );

			GL.Begin ( PrimitiveType.Quads );
			GL.Color3 ( color );


			GL.Vertex3 ( ( new Vector3d ( 1.0f, 1.0f, -1.0f ) * size + position ) );  // Top Right Of The Quad (Top)
			GL.Vertex3 ( ( new Vector3d ( -1.0f, 1.0f, -1.0f ) * size + position ) ); // Top Left Of The Quad (Top)
			GL.Vertex3 ( ( new Vector3d ( -1.0f, 1.0f, 1.0f ) * size + position ) );  // Bottom Left Of The Quad (Top)
			GL.Vertex3 ( ( new Vector3d ( 1.0f, 1.0f, 1.0f ) * size + position ) );   // Bottom Right Of The Quad (Top)

			GL.Vertex3 ( ( new Vector3d ( 1.0f, -1.0f, 1.0f ) * size + position ) );   // Top Right Of The Quad (Bottom)
			GL.Vertex3 ( ( new Vector3d ( -1.0f, -1.0f, 1.0f ) * size + position ) );  // Top Left Of The Quad (Bottom)
			GL.Vertex3 ( ( new Vector3d ( -1.0f, -1.0f, -1.0f ) * size + position ) ); // Bottom Left Of The Quad (Bottom)
			GL.Vertex3 ( ( new Vector3d ( 1.0f, -1.0f, -1.0f ) * size + position ) );  // Bottom Right Of The Quad (Bottom)

			GL.Vertex3 ( ( new Vector3d ( 1.0f, 1.0f, 1.0f ) * size + position ) );   // Top Right Of The Quad (Front)
			GL.Vertex3 ( ( new Vector3d ( -1.0f, 1.0f, 1.0f ) * size + position ) );  // Top Left Of The Quad (Front)
			GL.Vertex3 ( ( new Vector3d ( -1.0f, -1.0f, 1.0f ) * size + position ) ); // Bottom Left Of The Quad (Front)
			GL.Vertex3 ( ( new Vector3d ( 1.0f, -1.0f, 1.0f ) * size + position ) );  // Bottom Right Of The Quad (Front)

			GL.Vertex3 ( ( new Vector3d ( 1.0f, -1.0f, -1.0f ) * size + position ) );  // Bottom Left Of The Quad (Back)
			GL.Vertex3 ( ( new Vector3d ( -1.0f, -1.0f, -1.0f ) * size + position ) ); // Bottom Right Of The Quad (Back)
			GL.Vertex3 ( ( new Vector3d ( -1.0f, 1.0f, -1.0f ) * size + position ) );  // Top Right Of The Quad (Back)
			GL.Vertex3 ( ( new Vector3d ( 1.0f, 1.0f, -1.0f ) * size + position ) );   // Top Left Of The Quad (Back)

			GL.Vertex3 ( ( new Vector3d ( -1.0f, 1.0f, 1.0f ) * size + position ) );   // Top Right Of The Quad (Left)
			GL.Vertex3 ( ( new Vector3d ( -1.0f, 1.0f, -1.0f ) * size + position ) );  // Top Left Of The Quad (Left)
			GL.Vertex3 ( ( new Vector3d ( -1.0f, -1.0f, -1.0f ) * size + position ) ); // Bottom Left Of The Quad (Left)
			GL.Vertex3 ( ( new Vector3d ( -1.0f, -1.0f, 1.0f ) * size + position ) );  // Bottom Right Of The Quad (Left)

			GL.Vertex3 ( ( new Vector3d ( 1.0f, 1.0f, -1.0f ) * size + position ) );  // Top Right Of The Quad (Right)
			GL.Vertex3 ( ( new Vector3d ( 1.0f, 1.0f, 1.0f ) * size + position ) );   // Top Left Of The Quad (Right)
			GL.Vertex3 ( ( new Vector3d ( 1.0f, -1.0f, 1.0f ) * size + position ) );  // Bottom Left Of The Quad (Right)
			GL.Vertex3 ( ( new Vector3d ( 1.0f, -1.0f, -1.0f ) * size + position ) ); // Bottom Right Of The Quad (Right)

			GL.End ();

			triangleCounter += 12;
		}

		/// <summary>
		/// Renders cuboid (of uniform color) based on 2 opposite corners (with sides parallel to axes) and transforms it with transformation matrix
		/// </summary>
		/// <param name="c1">Position of first corner</param>
		/// <param name="c2">Position of corner opposite to first corner</param>
		/// <param name="transformation">4x4 transformation matrix</param>
		/// <param name="color">Uniform color of cuboid</param>
		private void RenderCuboid ( Vector3d c1, Vector3d c2, Matrix4d transformation, Color color )
		{
			SetVertexPointer ( false );
			SetVertexAttrib ( false );

			GL.PolygonMode ( checkTwosided.Checked ? MaterialFace.FrontAndBack : MaterialFace.Front,
							 WireframeBoundingBoxesCheckBox.Checked ? PolygonMode.Line : PolygonMode.Fill );

			GL.Begin ( PrimitiveType.Quads );
			GL.Color3 ( color );


			GL.Vertex3 ( RayVisualizer.AxesCorrector ( ApplyTransformation ( new Vector3d ( c2.X, c2.Y, c1.Z ), transformation ) ) ); // Top Right Of The Quad (Top)
			GL.Vertex3 ( RayVisualizer.AxesCorrector ( ApplyTransformation ( new Vector3d ( c1.X, c2.Y, c1.Z ), transformation ) ) ); // Top Left Of The Quad (Top)
			GL.Vertex3 ( RayVisualizer.AxesCorrector ( ApplyTransformation ( new Vector3d ( c1.X, c2.Y, c2.Z ), transformation ) ) ); // Bottom Left Of The Quad (Top)
			GL.Vertex3 ( RayVisualizer.AxesCorrector ( ApplyTransformation ( new Vector3d ( c2.X, c2.Y, c2.Z ), transformation ) ) ); // Bottom Right Of The Quad (Top)

			GL.Vertex3 ( RayVisualizer.AxesCorrector ( ApplyTransformation ( new Vector3d ( c2.X, c1.Y, c2.Z ), transformation ) ) ); // Top Right Of The Quad (Bottom)
			GL.Vertex3 ( RayVisualizer.AxesCorrector ( ApplyTransformation ( new Vector3d ( c1.X, c1.Y, c2.Z ), transformation ) ) ); // Top Left Of The Quad (Bottom)
			GL.Vertex3 ( RayVisualizer.AxesCorrector ( ApplyTransformation ( new Vector3d ( c1.X, c1.Y, c1.Z ), transformation ) ) ); // Bottom Left Of The Quad (Bottom)
			GL.Vertex3 ( RayVisualizer.AxesCorrector ( ApplyTransformation ( new Vector3d ( c2.X, c1.Y, c1.Z ), transformation ) ) ); // Bottom Right Of The Quad (Bottom)

			GL.Vertex3 ( RayVisualizer.AxesCorrector ( ApplyTransformation ( new Vector3d ( c2.X, c2.Y, c2.Z ), transformation ) ) ); // Top Right Of The Quad (Front)
			GL.Vertex3 ( RayVisualizer.AxesCorrector ( ApplyTransformation ( new Vector3d ( c1.X, c2.Y, c2.Z ), transformation ) ) ); // Top Left Of The Quad (Front)
			GL.Vertex3 ( RayVisualizer.AxesCorrector ( ApplyTransformation ( new Vector3d ( c1.X, c1.Y, c2.Z ), transformation ) ) ); // Bottom Left Of The Quad (Front)
			GL.Vertex3 ( RayVisualizer.AxesCorrector ( ApplyTransformation ( new Vector3d ( c2.X, c1.Y, c2.Z ), transformation ) ) ); // Bottom Right Of The Quad (Front)

			GL.Vertex3 ( RayVisualizer.AxesCorrector ( ApplyTransformation ( new Vector3d ( c2.X, c1.Y, c1.Z ), transformation ) ) ); // Bottom Left Of The Quad (Back)
			GL.Vertex3 ( RayVisualizer.AxesCorrector ( ApplyTransformation ( new Vector3d ( c1.X, c1.Y, c1.Z ), transformation ) ) ); // Bottom Right Of The Quad (Back)
			GL.Vertex3 ( RayVisualizer.AxesCorrector ( ApplyTransformation ( new Vector3d ( c1.X, c2.Y, c1.Z ), transformation ) ) ); // Top Right Of The Quad (Back)
			GL.Vertex3 ( RayVisualizer.AxesCorrector ( ApplyTransformation ( new Vector3d ( c2.X, c2.Y, c1.Z ), transformation ) ) ); // Top Left Of The Quad (Back)

			GL.Vertex3 ( RayVisualizer.AxesCorrector ( ApplyTransformation ( new Vector3d ( c1.X, c2.Y, c2.Z ), transformation ) ) ); // Top Right Of The Quad (Left)
			GL.Vertex3 ( RayVisualizer.AxesCorrector ( ApplyTransformation ( new Vector3d ( c1.X, c2.Y, c1.Z ), transformation ) ) ); // Top Left Of The Quad (Left)
			GL.Vertex3 ( RayVisualizer.AxesCorrector ( ApplyTransformation ( new Vector3d ( c1.X, c1.Y, c1.Z ), transformation ) ) ); // Bottom Left Of The Quad (Left)
			GL.Vertex3 ( RayVisualizer.AxesCorrector ( ApplyTransformation ( new Vector3d ( c1.X, c1.Y, c2.Z ), transformation ) ) ); // Bottom Right Of The Quad (Left)

			GL.Vertex3 ( RayVisualizer.AxesCorrector ( ApplyTransformation ( new Vector3d ( c2.X, c2.Y, c1.Z ), transformation ) ) ); // Top Right Of The Quad (Right)
			GL.Vertex3 ( RayVisualizer.AxesCorrector ( ApplyTransformation ( new Vector3d ( c2.X, c2.Y, c2.Z ), transformation ) ) ); // Top Left Of The Quad (Right)
			GL.Vertex3 ( RayVisualizer.AxesCorrector ( ApplyTransformation ( new Vector3d ( c2.X, c1.Y, c2.Z ), transformation ) ) ); // Bottom Left Of The Quad (Right)
			GL.Vertex3 ( RayVisualizer.AxesCorrector ( ApplyTransformation ( new Vector3d ( c2.X, c1.Y, c1.Z ), transformation ) ) ); // Bottom Right Of The Quad (Right)


			GL.End ();

			GL.PolygonMode ( checkTwosided.Checked ? MaterialFace.FrontAndBack : MaterialFace.Front,
							 checkWireframe.Checked ? PolygonMode.Line : PolygonMode.Fill );

			triangleCounter += 12;
		}

		/// <summary>
		/// Applies transformation matrix to vector
		/// </summary>
		/// <param name="vector">1x3 vector</param>
		/// <param name="transformation">4x4 transformation matrix</param>
		/// <returns>Transformed vector 1x3</returns>
		public Vector3d ApplyTransformation ( Vector3d vector, Matrix4d transformation )
		{
			Vector4d transformedVector = MultiplyVectorByMatrix ( new Vector4d ( vector, 1 ), transformation ); //( vector, 1 ) is extenstion [x  y  z] -> [x  y  z  1]

			return new Vector3d ( transformedVector.X / transformedVector.W, //[x  y  z  w] -> [x/w  y/w  z/w]
								  transformedVector.Y / transformedVector.W,
								  transformedVector.Z / transformedVector.W );
		}

		/// <summary>
		/// Multiplication of Vector4d and Matrix4d
		/// </summary>
		/// <param name="vector">Vector 1x4 on left side</param>
		/// <param name="matrix">Matrix 4x4 on right side</param>
		/// <returns>Result of multiplication - 1x4 vector</returns>
		public Vector4d MultiplyVectorByMatrix ( Vector4d vector, Matrix4d matrix )
		{
			Vector4d result = new Vector4d (0, 0, 0, 0);

			for ( int i = 0; i < 4; i++ )
			{
				for ( int j = 0; j < 4; j++ )
				{
					result[i] += vector[j] * matrix[j, i];
				}
			}

			return result;
		}

		/// <summary>
		/// Renders scene using bounding boxes
		/// </summary>
		private void BoundingBoxesVisualization ()
		{
			if ( sceneObjects == null || sceneObjects.Count == 0 || !BoundingBoxesCheckBox.Checked )
			{
				return;
			}

			int index = 0;

			foreach ( SceneObject sceneObject in sceneObjects )
			{
				if ( sceneObject.solid is Plane plane )
				{
					plane.GetBoundingBox ( out Vector3d c1, out Vector3d c2 );


					Color color = Color.FromArgb ( (int) ( sceneObject.color [ 0 ] * 255 ),
										 (int) ( sceneObject.color [ 1 ] * 255 ),
										 (int) ( sceneObject.color [ 2 ] * 255 ) );

					c1 = RayVisualizer.AxesCorrector ( c1 );
					c2 = RayVisualizer.AxesCorrector ( c2 );

					SetVertexPointer ( false );
					SetVertexAttrib ( false );



					if ( !plane.Triangle )
					{
						GL.Begin ( PrimitiveType.Quads );
						GL.Color3 ( color );

						GL.Vertex3 ( RayVisualizer.AxesCorrector ( ApplyTransformation ( new Vector3d ( c1.X, c1.Y, c2.Z ), sceneObject.transformation ) ) );
						GL.Vertex3 ( RayVisualizer.AxesCorrector ( ApplyTransformation ( new Vector3d ( c2.X, c1.Y, c2.Z ), sceneObject.transformation ) ) );
						GL.Vertex3 ( RayVisualizer.AxesCorrector ( ApplyTransformation ( new Vector3d ( c2.X, c2.Y, c1.Z ), sceneObject.transformation ) ) );
						GL.Vertex3 ( RayVisualizer.AxesCorrector ( ApplyTransformation ( new Vector3d ( c1.X, c2.Y, c1.Z ), sceneObject.transformation ) ) );

						triangleCounter += 2;

						GL.End ();
					}
					else
					{
						GL.Begin ( PrimitiveType.Triangles );
						GL.Color3 ( color );

						GL.Vertex3 ( RayVisualizer.AxesCorrector ( ApplyTransformation ( new Vector3d ( c1.X, c1.Y, c2.Z ), sceneObject.transformation ) ) );
						GL.Vertex3 ( RayVisualizer.AxesCorrector ( ApplyTransformation ( new Vector3d ( c2.X, c1.Y, c2.Z ), sceneObject.transformation ) ) );
						GL.Vertex3 ( RayVisualizer.AxesCorrector ( ApplyTransformation ( new Vector3d ( c2.X, c2.Y, c1.Z ), sceneObject.transformation ) ) );

						triangleCounter += 1;

						GL.End ();
					}
				}
				else
				{
					sceneObject.solid.GetBoundingBox ( out Vector3d c1, out Vector3d c2 );

					Color color;

					if ( sceneObject.color != null )
					{
						color = Color.FromArgb ( (int) ( sceneObject.color[0] * 255 ),
												 (int) ( sceneObject.color[1] * 255 ),
												 (int) ( sceneObject.color[2] * 255 ) );
					}
					else
					{
						color = GenerateColor ( index, sceneObjects.Count );
					}

					RenderCuboid ( c1, c2, sceneObject.transformation, color );
				}

				index++;
			}
		}

		/// <summary>
		/// Linearly generates shade of gray between approximately [0.25, 0.25, 0.25] and [0.75, 0.75, 0.75] (in 0 to 1 for RGB channels)
		/// Closer the currentValue is to the maxValue, closer is returned color to [0.75, 0.75, 0.75]
		/// </summary>
		/// <param name="currentValue">Indicates current position between 0 and maxValue</param>
		/// <param name="maxValue">Max value currentValue can have</param>
		/// <returns>Gray color between approximately [0.25, 0.25, 0.25] and [0.75, 0.75, 0.75]</returns>
		private Color GenerateColor ( int currentValue, int maxValue )
		{
			Arith.Clamp ( currentValue, 0, maxValue );

			int colorValue = (int) ( ( currentValue / (double) maxValue / 1.333 + 0.25 ) * 255 );

			return Color.FromArgb ( colorValue, colorValue, colorValue );
		}

    private List<SceneObject> sceneObjects;
    private IRayScene rayScene;
		/// <summary>
		/// Fills sceneObjects list with objects from current scene
		/// </summary>
		private void FillSceneObjects ()
		{
			if ( RayVisualizer.rayScene == rayScene ) // prevents filling whole list in case scene did not change (most of the time)
				return;
			else
				rayScene = RayVisualizer.rayScene;

      if ( !( rayScene.Intersectable is DefaultSceneNode root ) )
      {
        sceneObjects = null;
        return;
      }

      sceneObjects = new List<SceneObject> ();

			Matrix4d transformation = Matrix4d.Identity;

			double[] color;

			if ( (double[]) root.GetAttribute ( PropertyName.COLOR ) != null )
			{
				color = (double[]) root.GetAttribute ( PropertyName.COLOR );
			}
			else
			{
				color = ( (IMaterial) root.GetAttribute ( PropertyName.MATERIAL ) ).Color;
			}

			EvaluateSceneNode ( root, transformation, color );
		}

		/// <summary>
		/// Recursively goes through all children nodes of parent
		/// If child is ISolid, adds a new object to sceneObjects based on it
		/// If child is another CSGInnerNode, recursively goes through its children
		/// Meanwhile counts transformation matrix for SceneObjects
		/// </summary>
		/// <param name="parent">Parent node</param>
		/// <param name="transformation"></param>
		private void EvaluateSceneNode ( DefaultSceneNode parent, Matrix4d transformation, double[] color )
		{
			foreach ( ISceneNode sceneNode in parent.Children )
			{
			  ISceneNode children = (DefaultSceneNode) sceneNode;
			  Matrix4d localTransformation = children.ToParent * transformation;


				double[] newColor;

				// take color from child's attribute COLOR, child's attribute MATERIAL.Color or from parent
				if ( (double[]) children.GetAttribute ( PropertyName.COLOR ) == null )
				  newColor = ( (IMaterial) children.GetAttribute ( PropertyName.MATERIAL ) ).Color ?? color;
				else
					newColor = (double[]) children.GetAttribute ( PropertyName.COLOR );


				if ( children is ISolid solid )
				{
					sceneObjects.Add ( new SceneObject ( solid, localTransformation, newColor ) );
					continue;
				}

				if ( children is CSGInnerNode node )
				{
					EvaluateSceneNode ( node, localTransformation, newColor );
				}
			}
		}

		/// <summary>
		/// Data class containing info about ISolid and its absolute position in scene (through transformation matrix)
		/// </summary>
		private class SceneObject
		{
			public ISolid solid;
			public Matrix4d transformation;
			public double[] color;

			public SceneObject ( ISolid solid, Matrix4d transformation, double[] color )
			{
				this.solid = solid;
				this.transformation = transformation;
				this.color = color;
			}
		}

    private PointCloud pointCloud;
    private int pointCloudVBO;

    /// <summary>
    /// Gets reference to point cloud, calls initialization of VBO for point cloud and changes GUI elements respectively 
    /// </summary>
    /// <param name="sender">Not needed</param>
    /// <param name="e">Not needed</param>
    private void PointCloudButton_Click ( object sender, EventArgs e )
    {
      pointCloud = AdditionalViews.singleton.pointCloud;

      if ( pointCloud.cloud == null || pointCloud.IsCloudEmpty )
        return;

      InitializePointCloudVBO ();

      PointCloudCheckBox.Enabled = true;
      PointCloudCheckBox.Checked = true;
    }

    /// <summary>
    /// Initializes Vertex Buffer Object (VBO) for point cloud
    /// </summary>
    private void InitializePointCloudVBO ()
    {
      pointCloudVBO = GL.GenBuffer ();
      GL.BindBuffer ( BufferTarget.ArrayBuffer, pointCloudVBO );

      int size = pointCloud.numberOfElements * 9 * sizeof(float);

      GL.BufferData ( BufferTarget.ArrayBuffer, (IntPtr) ( size ), IntPtr.Zero, BufferUsageHint.StaticDraw );

      int currentLength = 0;
      foreach ( List<float> list in pointCloud.cloud )
      {      
        GL.BufferSubData ( BufferTarget.ArrayBuffer, (IntPtr) currentLength, (IntPtr) ( list.Count * sizeof ( float ) ) - 1, list.ToArray () );
        currentLength += list.Count * sizeof ( float );
      }

      BoundingBoxesCheckBox.Checked = false;
      WireframeBoundingBoxesCheckBox.Enabled = false;
    }

    private void BoundingBoxesCheckBox_CheckedChanged ( object sender, EventArgs e )
    {
      WireframeBoundingBoxesCheckBox.Enabled = BoundingBoxesCheckBox.Checked;
    }
  }
}