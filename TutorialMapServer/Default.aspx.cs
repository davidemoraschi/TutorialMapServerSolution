using System;
using System.Collections;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Web;
using System.Web.SessionState;
using System.Web.UI;
using System.Web.UI.WebControls;
using System.Web.UI.HtmlControls;
using System.Data.OleDb; //for dbf connection
using System.IO; //for copying the point shapefile
using System.Globalization;
using System.Threading;

namespace TutorialMapServer
{
	/// <summary>
	/// User Interface for c# MapServer Tutorial
	/// </summary>
	public class _Default : System.Web.UI.Page
	{
		protected System.Web.UI.WebControls.Literal litIdentifyResult;
		protected System.Web.UI.WebControls.DropDownList ddlLayers;
		protected System.Web.UI.WebControls.Button butFullExtent;
		protected System.Web.UI.WebControls.RadioButtonList rblGisTools;
		protected System.Web.UI.WebControls.ImageButton ibMap;
		protected System.Web.UI.WebControls.Button butRefresh;
		protected System.Web.UI.WebControls.CheckBoxList cblLayers;
		protected System.Web.UI.WebControls.Label lblInfo;
		protected System.Web.UI.WebControls.TextBox txtUser;
		protected System.Web.UI.WebControls.Button butClear;
		//private variable for this class
		mapObj map;
	
		/// <summary>
		/// Page Load of Tutorial User Interface
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void Page_Load(object sender, System.EventArgs e)
		{
			if(!Page.IsPostBack) //First access to the map
			{
				//send image stream from MapServer to ibMap
				ibMap.ImageUrl = "MapStream.aspx?ACTION=INITMAP";
				//initialize controls
				mapObj map = new mapObj(System.Configuration.ConfigurationSettings.AppSettings["mapFilePath"].ToString());
				//iterate the map layer to populate ddlLayer and cblLayer
				for(int i=0;i<map.numlayers;i++)
				{
					layerObj layer = map.getLayer(i);
					ddlLayers.Items.Add(layer.name);
					cblLayers.Items.Add(layer.name);
					//If this condition is true, the layer is visible
					if(layer.status==(int)mapscript.MS_ON)
					{
						cblLayers.Items[i].Selected = true;
					}
				}
			}
			else //Next accesses to the map, let's get it from session
			{
				map = (mapObj)Session["MAP"];
			}
		}

		/// <summary>
		/// Click Event on the Map button control
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void ibMap_Click(object sender, System.Web.UI.ImageClickEventArgs e)
		{
			lblInfo.Text = "";
			String Action = "";
			String activeLayer=ddlLayers.SelectedItem.Text;
			//we have to check what GIS tool is needed
			switch(rblGisTools.SelectedItem.Text.ToUpper())
			{
				case "ZOOM IN":
					Action = "ZOOMIN";
					break;
				case "ZOOM OUT":
					Action = "ZOOMOUT";
					break;
				case "IDENTIFY":
					Action = "IDENTIFY";
					break;
				case "ADD POINT":
					Action = "ADDPOINT";
					break;
			}
			//For Identify let's call DoIdentify
			if(Action.Equals("IDENTIFY"))
			{
				DoIdentify(e.X,e.Y,activeLayer);
			}
			//For Add Point let's call AddPoint
			if(Action.Equals("ADDPOINT"))
			{
				String[,] fieldValues = new String[2,2];
				fieldValues[0,0]="POI_USER";
				fieldValues[0,1]=	txtUser.Text;
				fieldValues[1,0]="POI_TIME";
				fieldValues[1,1]= DateTime.Now.ToString();
				AddPoint(e.X,e.Y,activeLayer,fieldValues);
			}
			//Stream map image to ibMap according to the needed GIS Action
			ibMap.ImageUrl = "MapStream.aspx?ACTION=" + Action + "&X=" + e.X + "&Y=" + e.Y + "&ACTIVELAYER=" + activeLayer;
		}

		/// <summary>
		/// Create a full Extent Map
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void butFullExtent_Click(object sender, System.EventArgs e)
		{
			ibMap.ImageUrl = "MapStream.aspx?ACTION=FULLEXTENT";
		}

		/// <summary>
		/// Add a point feature to point shapefile with an array of values for dbf, or add a point and attributes to a point PostGIS feature class
		/// </summary>
		/// <param name="x">x image coordinate</param>
		/// <param name="y">y image coordinate</param>
		/// <param name="activeLayer">name of the active layer</param>
		/// <param name="fieldValues">array with field names and values</param>
		private void AddPoint(Double x, Double y, String activeLayer, String[,] fieldValues)
		{
      //check if the active layer is a point layer and if the point layer is from a shapefile or from PostGIS
      layerObj layer = map.getLayerByName(activeLayer);
      if(layer.type!=MS_LAYER_TYPE.MS_LAYER_POINT)
      {
        //notify action
        lblInfo.Text = "This action can be performed only on point layers.";
        return;
      }
      //convert the image point in map point
      pointObj point = pixel2point(new pointObj(x,y,0,0)); 
      //generate the sql INSERT statment
      //get field list and value list to use in the query on dbf
      String fieldList = "";
      String valueList = "";
      for(int i=0; i<(fieldValues.Length/2); i++)
      {
        fieldList = fieldList + fieldValues[i,0];
        valueList = valueList + "'" + fieldValues[i,1] + "'";
        if(i<((fieldValues.Length/2)-1))
        {
          fieldList = fieldList + ", ";
          valueList = valueList + ", ";
        }
      }
      //add the point to a shapefile
      if(layer.connectiontype==MS_CONNECTION_TYPE.MS_SHAPEFILE)
      {
        String shapeFullPath = map.shapepath + "\\" + activeLayer + ".shp";
        shapefileObj shapefile = new shapefileObj(shapeFullPath,-2);
        /*Alternative way to insert a point in the shapefile using shapeObj:
        //create line to store the point
        lineObj line = new lineObj();
        line.add(point);
        //create shape
        shapeObj shape = new shapeObj((int)MS_SHAPE_TYPE.MS_SHAPE_POINT);
        shape.add(line);
        //add shape to shapefile
        shapefile.add(shape);
        */
        shapefile.addPoint(point);
        //add record for dbf table
        String sqlInsert = "INSERT INTO " + activeLayer + " (" + fieldList + ") VALUES(" + valueList + ")";
        OleDbConnection cn = new OleDbConnection(@"Provider=Microsoft.Jet.OLEDB.4.0;Data Source=" + map.shapepath + ";Extended Properties=dBASE IV;User ID=Admin;Password=");
        cn.Open();
        OleDbCommand com = cn.CreateCommand();
        com.CommandText = sqlInsert;
        com.CommandType = CommandType.Text;
        com.ExecuteNonQuery();
        cn.Close();
        shapefile.Dispose();
      }
      //add the point to a PostGIS table
      if(layer.connectiontype==MS_CONNECTION_TYPE.MS_POSTGIS)
      {
        //set CurrentCulture according to PostgreSQL server
        CultureInfo newCultureInfo = new CultureInfo("en-US");
        newCultureInfo.NumberFormat.NaNSymbol = "";
        Thread.CurrentThread.CurrentCulture = newCultureInfo;
        //connect with PostgreSQL
        //the sqlInsert includes also the geometry (with shapefile we need to make 2 different steps)
        String sqlInsert = "INSERT INTO " + activeLayer + " (" + fieldList + ",the_geom) VALUES(" + valueList + ", GeomFromText('POINT(" + point.x.ToString() + " " + point.y.ToString() + ")',-1))";
        //reads connection string for PostgreSQL
        String connectionString = System.Configuration.ConfigurationSettings.AppSettings["postgreSQLConnectionString"].ToString();
        Npgsql.NpgsqlConnection cn = new Npgsql.NpgsqlConnection(connectionString);
        cn.Open();
        Npgsql.NpgsqlCommand com = cn.CreateCommand();
        com.CommandText = sqlInsert;
        com.ExecuteNonQuery();
        cn.Close();
      }
      //notify action
      lblInfo.Text = "Point added to " + activeLayer + " point layer.";
		}

		/// <summary>
		/// Let's do identify
		/// </summary>
		/// <param name="x">x image coordinate for the point to identify</param>
		/// <param name="y">y image coordinate for the point to identify</param>
		/// <param name="activeLayer">layer to identify</param>
		private void DoIdentify(Double x, Double y, String activeLayer)
		{
			litIdentifyResult.Text = "";
			//identify
			layerObj layer = map.getLayerByName(activeLayer);
			if(layer!=null)
			{
				layer.template = "dummy"; //for historical reasons
				pointObj point = pixel2point(new pointObj(x,y,0,0)); //conver the image point in map point
				double tolerance = map.width/100; //we use this tolerance
				if(layer.queryByPoint(map, point, mapscript.MS_SINGLE, tolerance)==(int)MS_RETURN_VALUE.MS_SUCCESS)
				{
					//there is a feature to identify
					resultCacheObj result = layer.getResults();
					if(result.numresults>0)
					{
						int shapeInd = result.getResult(0).shapeindex;
						//int tileInd = result.getResult(0).tileindex;
						layer.open();
						shapeObj shape=layer.getFeature(shapeInd, -1);
						//iterate fields and getting values
						for(int i=0; i<layer.numitems; i++)
						{
							litIdentifyResult.Text += "<BR><B>" + layer.getItem(i) + "</B>=" + shape.getValue(i);
						}
						layer.close();
					}
				}
				else
				{
					//there is nothing to identify
					System.Diagnostics.Debug.WriteLine("Nothing to identify.");
				}
			}
		}

		/// <summary>
		/// Conver pixel point coordinates to map point coordinates
		/// </summary>
		/// <param name="pointPixel">pixel point (from map Image)</param>
		/// <returns></returns>
		private pointObj pixel2point(pointObj pointPixel)
		{
			rectObj extent = map.extent;
			double mapWidth = extent.maxx - extent.minx;
			double mapHeight = extent.maxy - extent.miny;
			double xperc;
			double yperc;
			xperc = pointPixel.x / map.width;
			yperc = (map.height-pointPixel.y) / map.height;
			double x=extent.minx + xperc*mapWidth;
			double y=extent.miny + yperc*mapHeight;
			pointObj pointMap = new pointObj(x,y,0,0);
			return pointMap;
		}

		/// <summary>
		/// Refresh the map
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void butRefresh_Click(object sender, System.EventArgs e)
		{
			//iterate layers and check visibility
			for(int i=0; i<cblLayers.Items.Count;i++)
			{
				layerObj layer = map.getLayerByName(cblLayers.Items[i].Text);
				if(cblLayers.Items[i].Selected)
				{
					layer.status=(int)mapscript.MS_ON;
				}
				else
				{
					layer.status=(int)mapscript.MS_OFF;
				}
			}
			//send image stream from MapServer to ibMap
			ibMap.ImageUrl = "MapStream.aspx?ACTION=REFRESHMAP";
		}

		/// <summary>
		/// Restore the original point shapefile or delete all the records from the PostGIS layer
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void butClear_Click(object sender, System.EventArgs e)
		{
			String shapeFullPath = map.shapepath + "\\" + ddlLayers.SelectedItem.Text + ".shp";
			layerObj layer = map.getLayerByName(ddlLayers.SelectedItem.Text);
      //action allowed only for point layer
      if(layer.type!=MS_LAYER_TYPE.MS_LAYER_POINT)
      {
        //notify action
        lblInfo.Text = "This action can be performed only on point layers.";
        return;
      }
      //different delete action if layer is shapefile or PostGIS
      //shapefile layer
			if(layer.connectiontype==MS_CONNECTION_TYPE.MS_SHAPEFILE)
			{
				//Clear the point shapefile by restoring its copy
				//Create a DirectoryInfo object representing the specified directory.
				DirectoryInfo dir = new DirectoryInfo(map.shapepath);
				//Get the FileInfo objects for every file that belongs to shapefile in the directory.
				FileInfo[] files = dir.GetFiles(ddlLayers.SelectedItem.Text + "Copy.*");
				for(int i=0; i<files.Length; i++)
				{
					//be sure to put a copy of the point shapefile under shapepath, the copy should be called as NameCopy (ie: for POI of this tutorial, we put a shapefile copy called POICopy)
					File.Copy(files[i].FullName, map.shapepath + "\\" + ddlLayers.SelectedItem.Text + files[i].Extension, true);
				}
				//notify action
				lblInfo.Text = "Shapefile cleared.";
			}
      //PostGIS layer
      if(layer.connectiontype==MS_CONNECTION_TYPE.MS_POSTGIS)
      {
        String connectionString = "Server=127.0.0.1;Port=5432;User Id=psqluser;password=psqluser;Database=TUTORIAL;";
        Npgsql.NpgsqlConnection cn = new Npgsql.NpgsqlConnection(connectionString);
        cn.Open();
        Npgsql.NpgsqlCommand com = cn.CreateCommand();
        com.CommandText = "DELETE FROM " + ddlLayers.SelectedItem.Text;
        com.ExecuteNonQuery();
        cn.Close();
        //notify action
        lblInfo.Text = "PostGIS layer cleared.";
      }
			ibMap.ImageUrl = "MapStream.aspx?ACTION=LAYERDELETE";
		}


		#region Web Form Designer generated code
		override protected void OnInit(EventArgs e)
		{
			//
			// CODEGEN: This call is required by the ASP.NET Web Form Designer.
			//
			InitializeComponent();
			base.OnInit(e);
		}
		
		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{    
			this.butRefresh.Click += new System.EventHandler(this.butRefresh_Click);
			this.ibMap.Click += new System.Web.UI.ImageClickEventHandler(this.ibMap_Click);
			this.butFullExtent.Click += new System.EventHandler(this.butFullExtent_Click);
			this.butClear.Click += new System.EventHandler(this.butClear_Click);
			this.Load += new System.EventHandler(this.Page_Load);

		}
		#endregion

	}
}
