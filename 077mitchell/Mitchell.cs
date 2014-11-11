﻿// Author: Josef Pelikan

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;
using OpenTK;
using Utilities;

namespace _077mitchell
{
  public partial class Form1 : Form
  {
    /// <summary>
    /// Initialize form parameters.
    /// </summary>
    private void InitializeParams ()
    {
      comboSampling.SelectedIndex = comboSampling.Items.IndexOf( "Random" );
      comboDensity.SelectedIndex  = comboDensity.Items.IndexOf( DefaultPdf.PDF_UNIFORM );
      densityFile                 = "";
      numericSamples.Value        = 1024;
      numericSeed.Value           = 12;
      numericResolution.Value     = 512;
      textParams.Text             = "k=6";
    }
  }

  /// <summary>
  /// Mitchell sampling by dart throwing.
  /// Number of candidates is defined by the parameter 'k'
  /// </summary>
  public class MitchellSampling : DefaultSampling, ISampling
  {
    /// <summary>
    /// Generate 'count' samples in the [0,1]x[0,1] domain.
    /// </summary>
    /// <param name="set">Output object.</param>
    /// <param name="count">Desired number of samples</param>
    /// <param name="param">Optional textual parameter set.</param>
    /// <returns>Actual number of generated samples.</returns>
    public int GenerateSampleSet ( SampleSet set, int count, string param = null )
    {
      Debug.Assert( set != null );

      // sampling parameters:
      int K = 5;
      bool toroid = true;
      if ( param != null )
      {
        string astr;
        Dictionary<string, string> p = Util.ParseKeyValueList( param );

        // d = <distance>
        if ( p.TryGetValue( "k", out astr ) )
        {
          int.TryParse( astr, out K );
          p.Remove( "k" );
        }
        if ( K < 1 ) K = 1;

        // toroid = {true|false}
        if ( p.TryGetValue( "toroid", out astr ) )
        {
          toroid = Util.positive( astr );
          p.Remove( "toroid" );
        }
      }

      set.samples.Clear();
      for ( int i = 0; i < count; i++ )
      {
        // generate one sample:
        double bestX = 0.0;
        double bestY = 0.0;   // best candidate so far
        double bestDD = 0.0;  // square distance of the best candidate

        int candidates = i * K;
        do
        {
          // one candidate:
          double x = rnd.UniformNumber();
          double y = rnd.UniformNumber();

          // compute its distance to the current sample-set:
          double DD = 4.0;   // current maximal distance squared (will decrease)
          double D = 2.0;    // current maximal distance

          if ( i == 0 ) break;
          bool checkX = toroid && (x < D || x > 1.0 - D);
          bool checkY = toroid && (y < D || y > 1.0 - D);

          foreach ( var s in set.samples )
          {
            bool dirty = false;    // need recompute D, checkX, checkY

            double DDx = (x - s.X) * (x - s.X);
            double DDy = (y - s.Y) * (y - s.Y);

            // plain distance:
            if ( DDx + DDy < DD )
            {
              DD = DDx + DDy;
              dirty = true;
            }

            // toroid:
            if ( checkX )
            {
              double save = DDx;
              DDx = (x - 1.0 - s.X) * (x - 1.0 - s.X);
              if ( DDx + DDy < DD )
              {
                DD = DDx + DDy;
                dirty = true;
              }
              DDx = (x + 1.0 - s.X) * (x + 1.0 - s.X);
              if ( DDx + DDy < DD )
              {
                DD = DDx + DDy;
                dirty = true;
              }
              DDx = save;
            }

            if ( checkY )
            {
              DDy = (y - 1.0 - s.Y) * (y - 1.0 - s.Y);
              if ( DDx + DDy < DD )
              {
                DD = DDx + DDy;
                dirty = true;
              }
              DDy = (y + 1.0 - s.Y) * (y + 1.0 - s.Y);
              if ( DDx + DDy < DD )
              {
                DD = DDx + DDy;
                dirty = true;
              }
            }

            if ( DD <= bestDD ) break;

            if ( dirty )
            {
              D = Math.Sqrt( DD );
              checkX = toroid && (x < D || x > 1.0 - D);
              checkY = toroid && (y < D || y > 1.0 - D);
            }
          }

          // DD is the farthest distance squared
          if ( DD > bestDD )
          {
            // we have the better candidate:
            bestDD = DD;
            bestX = x;
            bestY = y;
          }
        }
        while ( --candidates > 0 );

        set.samples.Add( new Vector2d( bestX, bestY ) );

        // User break check:
        if ( (i & 0xf) == 0 && userBreak ) break;
      }

      return set.samples.Count;
    }

    /// <summary>
    /// Sampling class identifier.
    /// </summary>
    public string Name
    {
      get { return "Mitchell"; }
    }
  }

  /// <summary>
  /// Density-controlled Mitchell sampling by dart throwing.
  /// Number of candidates is defined by the parameter 'k'.
  /// </summary>
  public class MitchellDensitySampling : DefaultSampling, ISampling
  {
    /// <summary>
    /// Generate 'count' samples in the [0,1]x[0,1] domain.
    /// </summary>
    /// <param name="set">Output object.</param>
    /// <param name="count">Desired number of samples</param>
    /// <param name="param">Optional textual parameter set.</param>
    /// <returns>Actual number of generated samples.</returns>
    public int GenerateSampleSet ( SampleSet set, int count, string param =null )
    {
      Debug.Assert( set != null );

      // sampling parameters:
      int K = 5;
      bool toroid = true;
      if ( param != null )
      {
        string astr;
        Dictionary<string, string> p = Util.ParseKeyValueList( param );

        // d = <distance>
        if ( p.TryGetValue( "k", out astr ) )
        {
          int.TryParse( astr, out K );
          p.Remove( "k" );
        }
        if ( K < 1 ) K = 1;

        // toroid = {true|false}
        if ( p.TryGetValue( "toroid", out astr ) )
        {
          toroid = Util.positive( astr );
          p.Remove( "toroid" );
        }
      }

      set.samples.Clear();
      if ( Density == null ) return 0;

      for ( int i = 0; i < count; i++ )
      {
        // generate one sample:
        double bestX = 0.0;
        double bestY = 0.0;    // best candidate so far
        double bestDD = -1.0;  // square distance of the best candidate

        int candidates = i * K;
        do
        {
          // one candidate:
          double x, y, density;
          do
          {
            Density.GetSample( out x, out y, rnd.UniformNumber(), rnd );
            // density at the sample point:
            density = Density.Pdf( x, y );
          }
          while ( density < 1e-4 );

          // compute its distance to the current sample-set:
          double DD = double.MaxValue;   // current maximal distance squared (will decrease)

          if ( i == 0 ) break;

          foreach ( var s in set.samples )
          {
            double DDx = (x - s.X ) * (x - s.X);
            double DDy = (y - s.Y ) * (y - s.Y);

            // plain distance:
            if ( DDx + DDy < DD )
              DD = DDx + DDy;

            // toroid:
            if ( toroid )
            {
              double save = DDx;
              DDx = (x - 1.0 - s.X) * (x - 1.0 - s.X);
              if ( DDx + DDy < DD )
                DD = DDx + DDy;
              DDx = (x + 1.0 - s.X) * (x + 1.0 - s.X);
              if ( DDx + DDy < DD )
                DD = DDx + DDy;
              DDx = save;

              DDy = (y - 1.0 - s.Y) * (y - 1.0 - s.Y);
              if ( DDx + DDy < DD )
                DD = DDx + DDy;
              DDy = (y + 1.0 - s.Y) * (y + 1.0 - s.Y);
              if ( DDx + DDy < DD )
                DD = DDx + DDy;
            }

            if ( DD * density <= bestDD ) break;
          }

          // DD is the farthest distance squared
          if ( DD * density > bestDD )
          {
            // we have the better candidate:
            bestDD = DD * density;
            bestX = x;
            bestY = y;
          }
        }
        while ( --candidates > 0 );

        set.samples.Add( new Vector2d( bestX, bestY ) );

        // User break check:
        if ( (i & 0xf) == 0 && userBreak ) break;
      }

      return set.samples.Count;
    }

    /// <summary>
    /// Sampling class identifier.
    /// </summary>
    public string Name
    {
      get { return "Mitchell density"; }
    }
  }
}